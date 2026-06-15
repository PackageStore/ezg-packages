using UnityEngine;

namespace Ezg.Package.RpgStats
{
    public class RPGEntity : MonoBehaviour
    {
        [SerializeField] private RPGEntityLevel _entityLevel;

        public RPGEntityLevel EntityLevel
        {
            get => _entityLevel;
            set => _entityLevel = value;
        }

        private void Awake()
        {
            if (EntityLevel == null)
            {
                EntityLevel = GetComponent<RPGEntityLevel>();
                if (EntityLevel == null) Debug.LogWarning("No RPGEntityLevel assigned to RPGEntity");
            }
        }
    }
}