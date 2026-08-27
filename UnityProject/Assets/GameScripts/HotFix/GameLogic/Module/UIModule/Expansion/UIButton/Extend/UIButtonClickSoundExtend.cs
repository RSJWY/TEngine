using TEngine;
using UnityEngine;
using AudioType = TEngine.AudioType;

namespace GameLogic
{
    [System.Serializable]
    public class UIButtonClickSoundExtend
    {
        [SerializeField] private bool m_isUseClickSound = true;
        [SerializeField] private string m_clickSoundLocation = "btn_click";

        public void OnPointerClick()
        {

        }

        public void OnPointerDown()
        {
            if (!m_isUseClickSound)
            {
                return;
            }

            if (!string.IsNullOrEmpty(m_clickSoundLocation))
            {
                GameModule.Audio.Play(AudioType.UISound, m_clickSoundLocation, bInPool: true);
            }
        }

        public void OnPointerUp()
        {

        }

        public void SetClickSoundLocation(string location)
        {
            m_clickSoundLocation = location;
        }
    }
}
