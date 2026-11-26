using System;
using System.Collections.Generic;
using BodycamHorror.Player;
using UnityEngine;

namespace BodycamHorror.Environment
{
    /// <summary>
    /// Something the player can use once they stand close and look at it (a fuse box to put a fuse in): the use key
    /// shows at <see cref="PromptPoint"/> (PlayerInteractor, E / D-pad down) and pressing it raises <see cref="Used"/>.
    /// What it does is up to whoever listens; switched off (enabled = false) it shows nothing.
    /// </summary>
    public class UseSpot : MonoBehaviour
    {
        public static readonly List<UseSpot> All = new List<UseSpot>();

        [Tooltip("Where the key prompt shows, in this object's space.")]
        public Vector3 promptOffset;
        [Tooltip("How close the player must be (m, from the camera). 0 = PlayerInteractor's reach.")]
        public float reach;

        /// <summary>Pressed by this player.</summary>
        public event Action<UseSpot, GameObject> Used;

        public bool CanUse => isActiveAndEnabled;
        public Vector3 PromptPoint => transform.TransformPoint(promptOffset);

        void OnEnable() => All.Add(this);
        void OnDisable() => All.Remove(this);
        void Start() => PlayerInteractor.Ensure();

        public void Use(GameObject player) => Used?.Invoke(this, player);

        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.9f, 1f, 0.7f);
            Gizmos.DrawWireSphere(PromptPoint, 0.04f);
        }
    }
}
