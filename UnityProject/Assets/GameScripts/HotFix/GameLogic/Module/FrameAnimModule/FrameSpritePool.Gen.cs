using System;
using System.Collections.Generic;
using UnityEngine;

namespace GameLogic
{
    /// <summary>
    /// <see cref="FrameSpritePool"/> 的生成等价手写补全。
    /// <para>原 DGame 由 FrameSpritePoolGenerator（Roslyn SourceGenerator）依据 FrameAnimName 枚举成员自动生成：
    /// 每个枚举成员生成一个 <c>public List&lt;Sprite&gt; Xxx</c> 字段（首字母大写），
    /// 以及 <c>GetSprites/AddSprite/SortAllSprites/SortSprite/ParseLastNumber</c> 方法。</para>
    /// <para>此文件为手写等价实现，不再依赖编译期生成器。新增 FrameAnimName 枚举成员时需同步在此补字段与 case。</para>
    /// </summary>
    public partial class FrameSpritePool
    {
        public List<Sprite> Idle = new List<Sprite>();
        public List<Sprite> Idle1 = new List<Sprite>();
        public List<Sprite> Idle2 = new List<Sprite>();
        public List<Sprite> Idle3 = new List<Sprite>();
        public List<Sprite> Idle4 = new List<Sprite>();
        public List<Sprite> Idle5 = new List<Sprite>();
        public List<Sprite> Run = new List<Sprite>();
        public List<Sprite> Run1 = new List<Sprite>();
        public List<Sprite> Run2 = new List<Sprite>();
        public List<Sprite> Run3 = new List<Sprite>();
        public List<Sprite> Run4 = new List<Sprite>();
        public List<Sprite> Run5 = new List<Sprite>();
        public List<Sprite> Attack = new List<Sprite>();
        public List<Sprite> Attack1 = new List<Sprite>();
        public List<Sprite> Attack2 = new List<Sprite>();
        public List<Sprite> Attack3 = new List<Sprite>();
        public List<Sprite> Attack4 = new List<Sprite>();
        public List<Sprite> Attack5 = new List<Sprite>();
        public List<Sprite> Walk = new List<Sprite>();
        public List<Sprite> Walk1 = new List<Sprite>();
        public List<Sprite> Walk2 = new List<Sprite>();
        public List<Sprite> Walk3 = new List<Sprite>();
        public List<Sprite> Walk4 = new List<Sprite>();
        public List<Sprite> Walk5 = new List<Sprite>();
        public List<Sprite> Death = new List<Sprite>();
        public List<Sprite> Death1 = new List<Sprite>();
        public List<Sprite> Death2 = new List<Sprite>();
        public List<Sprite> Death3 = new List<Sprite>();
        public List<Sprite> Death4 = new List<Sprite>();
        public List<Sprite> Death5 = new List<Sprite>();
        public List<Sprite> Appear = new List<Sprite>();
        public List<Sprite> Appear1 = new List<Sprite>();
        public List<Sprite> Appear2 = new List<Sprite>();
        public List<Sprite> Appear3 = new List<Sprite>();
        public List<Sprite> Appear4 = new List<Sprite>();
        public List<Sprite> Appear5 = new List<Sprite>();
        public List<Sprite> Skill = new List<Sprite>();
        public List<Sprite> Skill1 = new List<Sprite>();
        public List<Sprite> Skill2 = new List<Sprite>();
        public List<Sprite> Skill3 = new List<Sprite>();
        public List<Sprite> Skill4 = new List<Sprite>();
        public List<Sprite> Skill5 = new List<Sprite>();
        public List<Sprite> Hurt = new List<Sprite>();
        public List<Sprite> Hurt1 = new List<Sprite>();
        public List<Sprite> Hurt2 = new List<Sprite>();
        public List<Sprite> Hurt3 = new List<Sprite>();
        public List<Sprite> Hurt4 = new List<Sprite>();
        public List<Sprite> Hurt5 = new List<Sprite>();
        public List<Sprite> Loop = new List<Sprite>();
        public List<Sprite> Over = new List<Sprite>();
        public List<Sprite> Skill_prepare_loop = new List<Sprite>();
        public List<Sprite> Behit = new List<Sprite>();

        public List<Sprite> GetSprites(FrameAnimName animName)
        {
            List<Sprite> ret = null;
            switch (animName)
            {
                case FrameAnimName.idle:
                    ret = Idle;
                    break;
                case FrameAnimName.idle1:
                    ret = Idle1;
                    break;
                case FrameAnimName.idle2:
                    ret = Idle2;
                    break;
                case FrameAnimName.idle3:
                    ret = Idle3;
                    break;
                case FrameAnimName.idle4:
                    ret = Idle4;
                    break;
                case FrameAnimName.idle5:
                    ret = Idle5;
                    break;
                case FrameAnimName.run:
                    ret = Run;
                    break;
                case FrameAnimName.run1:
                    ret = Run1;
                    break;
                case FrameAnimName.run2:
                    ret = Run2;
                    break;
                case FrameAnimName.run3:
                    ret = Run3;
                    break;
                case FrameAnimName.run4:
                    ret = Run4;
                    break;
                case FrameAnimName.run5:
                    ret = Run5;
                    break;
                case FrameAnimName.attack:
                    ret = Attack;
                    break;
                case FrameAnimName.attack1:
                    ret = Attack1;
                    break;
                case FrameAnimName.attack2:
                    ret = Attack2;
                    break;
                case FrameAnimName.attack3:
                    ret = Attack3;
                    break;
                case FrameAnimName.attack4:
                    ret = Attack4;
                    break;
                case FrameAnimName.attack5:
                    ret = Attack5;
                    break;
                case FrameAnimName.walk:
                    ret = Walk;
                    break;
                case FrameAnimName.walk1:
                    ret = Walk1;
                    break;
                case FrameAnimName.walk2:
                    ret = Walk2;
                    break;
                case FrameAnimName.walk3:
                    ret = Walk3;
                    break;
                case FrameAnimName.walk4:
                    ret = Walk4;
                    break;
                case FrameAnimName.walk5:
                    ret = Walk5;
                    break;
                case FrameAnimName.death:
                    ret = Death;
                    break;
                case FrameAnimName.death1:
                    ret = Death1;
                    break;
                case FrameAnimName.death2:
                    ret = Death2;
                    break;
                case FrameAnimName.death3:
                    ret = Death3;
                    break;
                case FrameAnimName.death4:
                    ret = Death4;
                    break;
                case FrameAnimName.death5:
                    ret = Death5;
                    break;
                case FrameAnimName.appear:
                    ret = Appear;
                    break;
                case FrameAnimName.appear1:
                    ret = Appear1;
                    break;
                case FrameAnimName.appear2:
                    ret = Appear2;
                    break;
                case FrameAnimName.appear3:
                    ret = Appear3;
                    break;
                case FrameAnimName.appear4:
                    ret = Appear4;
                    break;
                case FrameAnimName.appear5:
                    ret = Appear5;
                    break;
                case FrameAnimName.skill:
                    ret = Skill;
                    break;
                case FrameAnimName.skill1:
                    ret = Skill1;
                    break;
                case FrameAnimName.skill2:
                    ret = Skill2;
                    break;
                case FrameAnimName.skill3:
                    ret = Skill3;
                    break;
                case FrameAnimName.skill4:
                    ret = Skill4;
                    break;
                case FrameAnimName.skill5:
                    ret = Skill5;
                    break;
                case FrameAnimName.hurt:
                    ret = Hurt;
                    break;
                case FrameAnimName.hurt1:
                    ret = Hurt1;
                    break;
                case FrameAnimName.hurt2:
                    ret = Hurt2;
                    break;
                case FrameAnimName.hurt3:
                    ret = Hurt3;
                    break;
                case FrameAnimName.hurt4:
                    ret = Hurt4;
                    break;
                case FrameAnimName.hurt5:
                    ret = Hurt5;
                    break;
                case FrameAnimName.loop:
                    ret = Loop;
                    break;
                case FrameAnimName.over:
                    ret = Over;
                    break;
                case FrameAnimName.skill_prepare_loop:
                    ret = Skill_prepare_loop;
                    break;
                case FrameAnimName.behit:
                    ret = Behit;
                    break;
            }
            return ret;
        }

        public void AddSprite(FrameAnimName animName, Sprite sprite)
        {
            var list = GetSprites(animName);
            list?.Add(sprite);
        }

        public void SortAllSprites()
        {
            SortSprite(Idle);
            SortSprite(Idle1);
            SortSprite(Idle2);
            SortSprite(Idle3);
            SortSprite(Idle4);
            SortSprite(Idle5);
            SortSprite(Run);
            SortSprite(Run1);
            SortSprite(Run2);
            SortSprite(Run3);
            SortSprite(Run4);
            SortSprite(Run5);
            SortSprite(Attack);
            SortSprite(Attack1);
            SortSprite(Attack2);
            SortSprite(Attack3);
            SortSprite(Attack4);
            SortSprite(Attack5);
            SortSprite(Walk);
            SortSprite(Walk1);
            SortSprite(Walk2);
            SortSprite(Walk3);
            SortSprite(Walk4);
            SortSprite(Walk5);
            SortSprite(Death);
            SortSprite(Death1);
            SortSprite(Death2);
            SortSprite(Death3);
            SortSprite(Death4);
            SortSprite(Death5);
            SortSprite(Appear);
            SortSprite(Appear1);
            SortSprite(Appear2);
            SortSprite(Appear3);
            SortSprite(Appear4);
            SortSprite(Appear5);
            SortSprite(Skill);
            SortSprite(Skill1);
            SortSprite(Skill2);
            SortSprite(Skill3);
            SortSprite(Skill4);
            SortSprite(Skill5);
            SortSprite(Hurt);
            SortSprite(Hurt1);
            SortSprite(Hurt2);
            SortSprite(Hurt3);
            SortSprite(Hurt4);
            SortSprite(Hurt5);
            SortSprite(Loop);
            SortSprite(Over);
            SortSprite(Skill_prepare_loop);
            SortSprite(Behit);
        }

        public void SortSprite(List<Sprite> sprites)
        {
            if (sprites == null)
            {
                return;
            }
            sprites.Sort((a, b) =>
            {
                int aNum = ParseLastNumber(a.name);
                int bNum = ParseLastNumber(b.name);
                return aNum.CompareTo(bNum);
            });
        }

        private int ParseLastNumber(ReadOnlySpan<char> spriteName)
        {
            int lastUnderscore = spriteName.LastIndexOf('_');
            if (lastUnderscore < 0)
            {
                return 0;
            }
            var numberSpan = spriteName.Slice(lastUnderscore + 1);
            return int.TryParse(numberSpan, out int result) ? result : 0;
        }
    }
}
