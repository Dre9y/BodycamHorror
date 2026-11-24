using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    public sealed class EncounterThreshold : MonoBehaviour
    {
        public GameObject encounter;

        void OnTriggerEnter(Collider other)
        {
            if (!other.GetComponentInParent<BodycamPlayerController>()) return;
            encounter.SetActive(true);
            gameObject.SetActive(false);
        }
    }
}
