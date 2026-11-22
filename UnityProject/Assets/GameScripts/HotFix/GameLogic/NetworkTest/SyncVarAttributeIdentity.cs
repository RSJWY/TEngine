using PurrNet;
using UnityEngine;

namespace GameLogic.NetworkTest
{
    /// <summary>运行时 SyncVar 测试对象：记录值收敛及 onChanged 回调。</summary>
    public sealed class SyncVarAttributeIdentity : NetworkIdentity
    {
        public const int InitialValue = 7;
        public const int FinalValue = 4242;

        [SerializeField] private SyncVar<int> _value = new(0, sendIntervalInSeconds: 0f, ownerAuth: false);

        private bool _subscribed;

        public static SyncVarAttributeIdentity LocalInstance { get; private set; }

        public int Value => _value.value;
        public int LastChangedValue { get; private set; }
        public int ChangeCount { get; private set; }

        public bool MatchesInitial()
        {
            return _value.value == InitialValue;
        }

        public bool MatchesFinal()
        {
            return _value.value == FinalValue && LastChangedValue == FinalValue && ChangeCount > 0;
        }

        public void RunServerOps()
        {
            _value.value = 100;
            _value.value = 200;
            _value.value = FinalValue;
        }

        public static void ResetAll()
        {
            LocalInstance = null;
        }

        protected override void OnEarlySpawn()
        {
            gameObject.SetActive(true);
        }

        protected override void OnSpawned(bool asServer)
        {
            LocalInstance = this;
            if (!_subscribed)
            {
                _value.onChanged += OnValueChanged;
                _subscribed = true;
            }

            if (asServer)
            {
                _value.value = InitialValue;
            }
        }

        protected override void OnDespawned(bool asServer)
        {
            if (_subscribed)
            {
                _value.onChanged -= OnValueChanged;
                _subscribed = false;
            }

            if (LocalInstance == this)
            {
                LocalInstance = null;
            }
        }

        private void OnValueChanged(int value)
        {
            LastChangedValue = value;
            ChangeCount++;
        }
    }
}
