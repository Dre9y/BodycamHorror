using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.Player
{
    /// <summary>
    /// First-person hands and gun sit centimetres from the chest flashlight, so the beam would blow
    /// them out. They render on their own URP rendering layer ("Light Layer 1"): the flashlight beam
    /// skips it and lights only the world, while scene lamps, the muzzle flash and a weak hand-fill
    /// light include it. A light that should also light the hands needs this layer in its Rendering Layers.
    /// </summary>
    public static class ViewModelLighting
    {
        public const uint WorldLayer = 1u << 0;       // "Default"
        public const uint ViewModelLayer = 1u << 1;   // "Light Layer 1"

        /// <summary>Puts every renderer under <paramref name="root"/> on the view-model layer only.</summary>
        public static void MarkViewModel(GameObject root)
        {
            foreach (var r in root.GetComponentsInChildren<Renderer>(true)) r.renderingLayerMask = ViewModelLayer;
        }

        /// <summary>Sets which rendering layers a light affects.</summary>
        public static void SetLightLayers(Light light, uint mask)
        {
            light.renderingLayerMask = (int)mask;
            var data = light.GetComponent<UniversalAdditionalLightData>();
            if (!data) data = light.gameObject.AddComponent<UniversalAdditionalLightData>();
            data.renderingLayers = mask;
        }

        /// <summary>Lets an existing light also reach the hands and gun.</summary>
        public static void AlsoLightViewModel(Light light) =>
            SetLightLayers(light, (uint)light.renderingLayerMask | ViewModelLayer);
    }
}
