// Copyright 2025 Code Philosophy
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using HybridCLR;
using HybridCLR.CodeGen.AOT;
using HybridCLR.CodeGen.MethodBridge;
using HybridCLR.Commands;
using HybridCLR.Installer;
using HybridCLR.Meta;
using HybridCLR.Mod;
using HybridCLR.Utils;
using Obfuz;
using Obfuz.Settings;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using CombinedAssemblyResolver = HybridCLR.Mod.CombinedAssemblyResolver;
using IAssemblyResolver = HybridCLR.Mod.IAssemblyResolver;

namespace Obfuz4HybridCLR
{
    public static class PrebuildCommandExt
    {
        public static string GetObfuscatedHotUpdateAssemblyOutputPath(BuildTarget target)
        {
            return $"{ObfuzSettings.Instance.ObfuzRootDir}/{target}/ObfuscatedHotUpdateAssemblies";
        }


        [MenuItem("HybridCLR/ObfuzExtension/GenerateAll")]
        public static void GenerateAll()
        {
            var installer = new InstallerController();
            if (!installer.HasInstalledHybridCLR())
            {
                throw new BuildFailedException($"You have not initialized HybridCLR, please install it via menu 'HybridCLR/Installer'");
            }
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            CompileDllCommand.CompileDll(target);
            Il2CppDefGeneratorCommand.GenerateIl2CppDef();
            LinkGeneratorCommand.GenerateLinkXml(target);
            StripAOTDllCommand.GenerateStripedAOTDlls(target);

            string obfuscatedHotUpdateDllPath = GetObfuscatedHotUpdateAssemblyOutputPath(target);
            ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);
            GenerateMethodBridgeAndReversePInvokeWrapper(target, obfuscatedHotUpdateDllPath);
            GenerateAOTGenericReference(target, obfuscatedHotUpdateDllPath);
        }

        [MenuItem("HybridCLR/ObfuzExtension/CompileAndObfuscateDll")]
        public static void CompileAndObfuscateDll()
        {
            BuildTarget target = EditorUserBuildSettings.activeBuildTarget;
            CompileDllCommand.CompileDll(target);

            string obfuscatedHotUpdateDllPath = GetObfuscatedHotUpdateAssemblyOutputPath(target);
            ObfuscateUtil.ObfuscateHotUpdateAssemblies(target, obfuscatedHotUpdateDllPath);
        }

        public static IAssemblyResolver CreateObfuscatedHotUpdateAssemblyResolver(BuildTarget target, List<string> obfuscatedHotUpdateAssemblies, string obfuscatedHotUpdateDllPath)
        {
            return new FixedSetAssemblyResolver(obfuscatedHotUpdateDllPath, obfuscatedHotUpdateAssemblies);
        }

        public static IAssemblyResolver CreateObfuscatedHotUpdateAndAOTAssemblyResolver(BuildTarget target, List<string> hotUpdateAssemblies, List<string> assembliesToObfuscate, string obfuscatedHotUpdateDllPath)
        {
            return new CombinedAssemblyResolver(
                CreateObfuscatedHotUpdateAssemblyResolver(target, hotUpdateAssemblies.Intersect(assembliesToObfuscate).ToList(), obfuscatedHotUpdateDllPath),
                MetaUtil.CreateHotUpdateAssemblyResolver(target, hotUpdateAssemblies.Except(assembliesToObfuscate).ToList()),
                MetaUtil.CreateAOTAssemblyResolver(target)
                );
        }

        public static void GenerateMethodBridgeAndReversePInvokeWrapper(BuildTarget target, string obfuscatedHotUpdateDllPath)
        {
            string aotDllDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
            List<string> aotAssemblyNames = Directory.Exists(aotDllDir) ?
                Directory.GetFiles(aotDllDir, "*.dll", SearchOption.TopDirectoryOnly).Select(Path.GetFileNameWithoutExtension).ToList()
                : new List<string>();
            if (aotAssemblyNames.Count == 0)
            {
                throw new Exception($"no aot assembly found. please run `HybridCLR/Generate/All` or `HybridCLR/Generate/AotDlls` to generate aot dlls before runing `HybridCLR/Generate/MethodBridge`");
            }
            AssemblyReferenceDeepCollector collector = new AssemblyReferenceDeepCollector(MetaUtil.CreateAOTAssemblyResolver(target), aotAssemblyNames);

            var methodBridgeAnalyzer = new MethodBridgeAnalyzer(new MethodBridgeAnalyzer.Options
            {
                MaxIterationCount = Math.Min(20, SettingsUtil.HybridCLRSettings.maxMethodBridgeGenericIteration),
                Collector = collector,
            });

            methodBridgeAnalyzer.Run();

            List<string> hotUpdateDlls = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;
            var cache = new AssemblyCache(CreateObfuscatedHotUpdateAndAOTAssemblyResolver(target, hotUpdateDlls, ObfuzSettings.Instance.assemblySettings.GetAssembliesToObfuscate(), obfuscatedHotUpdateDllPath));

            var reversePInvokeAnalyzer = new MonoPInvokeCallbackAnalyzer(cache, hotUpdateDlls);
            reversePInvokeAnalyzer.Run();

            var calliAnalyzer = new CalliAnalyzer(cache, hotUpdateDlls);
            calliAnalyzer.Run();

            string outputFile = $"{SettingsUtil.GeneratedCppDir}/MethodBridge.cpp";

            var callNativeMethodSignatures = calliAnalyzer.CalliMethodSignatures.ToList();

            MethodBridgeGeneratorCommand.GenerateMethodBridgeCppFile(methodBridgeAnalyzer.GenericMethods, reversePInvokeAnalyzer.ReversePInvokeMethods, callNativeMethodSignatures, outputFile);
        }

        public static void GenerateAOTGenericReference(BuildTarget target, string obfuscatedHotUpdateDllPath)
        {
            var gs = SettingsUtil.HybridCLRSettings;
            List<string> hotUpdateDllNames = SettingsUtil.HotUpdateAssemblyNamesExcludePreserved;

            AssemblyReferenceDeepCollector collector = new AssemblyReferenceDeepCollector(
                CreateObfuscatedHotUpdateAndAOTAssemblyResolver(target, hotUpdateDllNames, ObfuzSettings.Instance.assemblySettings.GetAssembliesToObfuscate(), obfuscatedHotUpdateDllPath), hotUpdateDllNames);
            var analyzer = new AOTReferenceAnalyzer(new AOTReferenceAnalyzer.Options
            {
                MaxIterationCount = Math.Min(20, gs.maxGenericReferenceIteration),
                Collector = collector,
            });

            analyzer.Run();

            var writer = new AOTGenericReferenceWriter();
            writer.Write(analyzer.AotGenericTypes.ToList(), analyzer.AotGenericMethods.ToList(), $"{Application.dataPath}/{gs.outputAOTGenericReferenceFile}");
            AssetDatabase.Refresh();
        }
    }
}
