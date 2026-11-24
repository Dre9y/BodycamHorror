using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BodycamHorror.EditorTools
{
    /// <summary>
    /// The item screen (UI.ItemShowcase) renders the item with a renderer of its own: the player's (PC_Renderer) puts the
    /// bodycam lens on everything (the fisheye focus and its vignette), and a gun on a studio stage must not look through
    /// it. This copies PC_Renderer without its features to Assets/Settings/Showcase_Renderer.asset and adds it to the
    /// pipeline asset's renderer list; ItemShowcase finds it there by name. Menu: BodycamHorror > Set Up Item Showcase.
    /// </summary>
    public static class ItemShowcaseSetup
    {
        const string Source = "Assets/Settings/PC_Renderer.asset";
        const string Target = "Assets/Settings/Showcase_Renderer.asset";

        [MenuItem("BodycamHorror/Set Up Item Showcase")]
        public static void SetUp()
        {
            var data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Target);
            if (!data)
            {
                AssetDatabase.CopyAsset(Source, Target);
                data = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(Target);
                foreach (var feature in data.rendererFeatures.ToArray())
                    if (feature) AssetDatabase.RemoveObjectFromAsset(feature);
                var so = new SerializedObject(data);
                so.FindProperty("m_RendererFeatures").ClearArray();
                so.FindProperty("m_RendererFeatureMap").ClearArray();
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(data);
            }

            // this project sets its pipeline per quality level, not in Graphics
            var pipeline = (QualitySettings.renderPipeline ?? GraphicsSettings.defaultRenderPipeline) as UniversalRenderPipelineAsset;
            if (!pipeline) { Debug.LogWarning("No URP asset to add the showcase renderer to."); return; }
            var pso = new SerializedObject(pipeline);
            var list = pso.FindProperty("m_RendererDataList");
            for (int i = 0; i < list.arraySize; i++)
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == data) { AssetDatabase.SaveAssets(); return; }
            list.arraySize++;
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = data;
            pso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(pipeline);
            AssetDatabase.SaveAssets();
            Debug.Log("Item showcase renderer added to " + pipeline.name);
        }
    }
}
