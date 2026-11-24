using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace BodycamHorror.EditorTools
{
    internal sealed class BasementTransitionBuilder
    {
        const string Basement = "Assets/Prefabs/Basement/";
        const string Materials = "Assets/Generated/Materials/Basement/Basement_";
        const float Floor = -1.6f, Ceiling = 2.1f;
        Transform root;
        Material brick, concrete, soffit, iron;

        // Update just the old cul-de-sac in the existing, hand-dressed scene.
        [MenuItem("BodycamHorror/Level/Add Basement Transition")]
        static void AddToLevel()
        {
            var landing = GameObject.Find("PvE_Landing");
            if (!landing || landing.scene.path != "Assets/Scenes/Level.unity" || landing.transform.Find("BasementTransition")) return;
            var vestibule = landing.transform.Find("Vestibule");
            if (!vestibule) return;
            Build(landing.transform);
            foreach (Transform piece in vestibule)
            {
                bool backWall = Mathf.Abs(piece.localPosition.z - 19.05f) < 0.01f &&
                    (piece.name == "Paint" || piece.name == "Plaster");
                if (backWall || piece.name.StartsWith("StepUp"))
                {
                    Undo.RecordObject(piece.gameObject, "Open basement passage");
                    piece.gameObject.SetActive(false);
                }
            }
            Undo.RegisterCreatedObjectUndo(landing.transform.Find("BasementTransition").gameObject, "Add basement transition");
            EditorSceneManager.MarkSceneDirty(landing.scene);
        }

        internal static void Build(Transform landing)
        {
            var b = new BasementTransitionBuilder();
            b.root = new GameObject("BasementTransition").transform;
            b.root.SetParent(landing, false);
            b.root.localPosition = new Vector3(2.25f, 0, 19.2f);
            b.brick = AssetDatabase.LoadAssetAtPath<Material>(Materials + "BrickPlaster.mat");
            b.concrete = AssetDatabase.LoadAssetAtPath<Material>(Materials + "FloorConcrete.mat");
            b.soffit = AssetDatabase.LoadAssetAtPath<Material>(Materials + "Ceiling.mat");
            b.iron = AssetDatabase.LoadAssetAtPath<Material>(Materials + "MetalPaint.mat");
            b.Architecture();
            b.Furnish();
            b.Puzzle();
        }

        void Architecture()
        {
            Flight("Entry", 0, 0, 0, 3.2f);
            Box("Hall floor", -6.2f, 6.2f, Floor - .2f, Floor, 4, 14, concrete);
            Box("Hall ceiling", -6.2f, 6.2f, Ceiling, Ceiling + .2f, 4, 14, soffit);
            Wall("West wall", -6.2f, -6, 4, 14);
            Wall("East wall", 6, 6.2f, 4, 14);
            Wall("Entry return L", -6, -1.6f, 4, 4.2f);
            Wall("Entry return R", 1.6f, 6, 4, 4.2f);
            Wall("Gallery south", 1.9f, 2.1f, 4, 6.2f);
            Wall("Gallery north", 1.9f, 2.1f, 8.6f, 14);
            Wall("Exit return L", -6, -3.2f, 13.8f, 14);
            Wall("Exit return R", -.8f, 6, 13.8f, 14);
            Flight("Basement", -2, 14, Floor, 2.4f);
            Box("Receiving bay floor", -5, 1, -3.4f, -3.2f, 18, 23, concrete);
            Box("Receiving bay ceiling", -5, 1, -.2f, 0, 18, 23, soffit);
            Box("Bay left", -5.2f, -5, -3.2f, 0, 18, 23.2f, brick);
            Box("Bay right", 1, 1.2f, -3.2f, 0, 18, 23.2f, brick);
            Box("Bay return L", -5, -3.2f, -3.2f, 0, 18, 18.2f, brick);
            Box("Bay return R", -.8f, 1, -3.2f, 0, 18, 18.2f, brick);
            Box("Bay end", -5, 1, -3.2f, 0, 23, 23.2f, brick);
        }

        void Flight(string name, float x, float z, float top, float width)
        {
            float left = x - width / 2, right = x + width / 2;
            for (int i = 0; i < 10; i++)
                Box(name + " tread " + (i+1), left, right, top - 1.8f, top - .16f * (i+1), z + .4f*i, z + .4f*(i+1), concrete);
            Box(name + " left", left-.2f, left, top-1.8f, top+2.9f, z, z+4, brick);
            Box(name + " right", right, right+.2f, top-1.8f, top+2.9f, z, z+4, brick);
            Box(name + " ceiling", left, right, top+2.7f, top+2.9f, z, z+4, soffit);
        }

        void Wall(string name, float x0, float x1, float z0, float z1) =>
            Box(name, x0, x1, Floor, Ceiling, z0, z1, brick);

        void Box(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material material)
        {
            var a = root.TransformPoint(x0,y0,z0);
            var b = root.TransformPoint(x1,y1,z1);
            var go = LightingTestSceneBuilder.Box(name, root, Vector3.Min(a,b), Vector3.Max(a,b), material, 2, 2);
            go.transform.rotation = Quaternion.identity;
        }

        GameObject Place(string path, float x, float y, float z, float yaw = 0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root);
            go.transform.localPosition = new Vector3(x,y,z);
            go.transform.localRotation = Quaternion.Euler(0,yaw,0);
            return go;
        }

        GameObject Prop(string name, float x, float y, float z, float yaw = 0) =>
            Place(Basement + name + ".prefab", x,y,z,yaw);

        void Lamp(float x, float y, float z, Color color)
        {
            var lamp = Prop("Lamp_Caged", x,y,z).GetComponentInChildren<Light>();
            lamp.color = color;
            lamp.shadows = LightShadows.None;
            lamp.range = 6f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(lamp);
        }

        void Furnish()
        {
            Prop("Workbench", -5.25f, Floor, 5.65f, 90);
            Prop("Radiator_Basement", -5.8f, Floor, 11.5f, 90);
            Prop("GasCylinder", .9f, Floor, 11.7f);
            Prop("Barrel", .75f, Floor, 12.6f);
            Prop("Shelf_Metal", 4.6f, Floor, 13.45f, 180);
            Prop("Bucket", 5.45f, Floor, 8.7f);
            Prop("Crate", -4.1f, -3.2f, 21.5f);
            Prop("Door_Bunker", -1.7f, -3.2f, 22.94f, 180);
            var amber = new Color(1,.69f,.36f);
            var cold = new Color(.55f,.78f,1);
            Lamp(0,2.7f,1.1f, amber);
            Lamp(-4.1f,Ceiling,8.4f, amber);
            Lamp(4.1f,Ceiling,10.5f,cold);
            Lamp(-2,Ceiling,12.9f, amber);
            Lamp(-2,-.2f,20.7f,cold);
            Sign("ТЕПЛОВОЙ УЗЕЛ\nПри прорыве пара перекрыть обе линии", 0,1.67f,4.42f,0,2.85f);
            // The entry sign hangs above the foot of the stairs.
            Box("Sign hanger", -.03f,.03f,1.92f,Ceiling,4.40f,4.44f,iron);
            Sign("I   /   ГАЛЕРЕЯ", -5.94f,.28f,8.5f,-90,1.55f);
            Sign("II   /   СПУСК", 5.94f,.28f,11.2f,90,1.55f);
            Sign("II   /   В ПОДВАЛ", -2,1.77f,13.8f,0,2.28f);
            Box("Exit sign lintel", -3.2f,-.8f,1.52f,Ceiling,13.8f,14,brick);
            Sign("−1   ПОДВАЛ", -3.7f,-1.45f,22.92f,0,1.7f);
        }

        void Sign(string message, float x,float y,float z,float yaw,float width)
        {
            var board = GameObject.CreatePrimitive(PrimitiveType.Cube);
            board.name = message.Split('\n')[0];
            board.transform.SetParent(root,false);
            board.transform.localPosition = new Vector3(x,y,z);
            board.transform.localRotation = Quaternion.Euler(0,yaw,0);
            board.transform.localScale = new Vector3(width,.48f,.045f);
            board.GetComponent<Renderer>().sharedMaterial = iron;

            var canvas = new GameObject("Lettering", typeof(RectTransform), typeof(Canvas));
            canvas.transform.SetParent(root,false);
            canvas.transform.localRotation = board.transform.localRotation;
            canvas.transform.position = board.transform.TransformPoint(0,0,-.55f);
            canvas.transform.localScale = Vector3.one * .002f;
            var rect = (RectTransform)canvas.transform;
            canvas.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
            rect.sizeDelta = new Vector2(width*500,230);
            var label = new GameObject("Stencil", typeof(RectTransform), typeof(Text)).GetComponent<Text>();
            label.transform.SetParent(rect,false);
            label.rectTransform.sizeDelta = rect.sizeDelta;
            label.font = AssetDatabase.LoadAssetAtPath<Font>("Assets/Fonts/Oswald.ttf");
            label.fontSize = message.Contains("\n") ? 70 : 86;
            label.alignment = TextAnchor.MiddleCenter;
            label.color = new Color(.88f,.84f,.69f);
            label.text = message;
        }

        void Puzzle()
        {
            var supply = Wheel(-5.78f,8.5f,90);
            var isolation = Wheel(5.78f,11.2f,-90);
            Jet("I - gallery", new Vector3(2,Floor+1,6.2f),0,supply);
            Jet("II - descent", new Vector3(-3.2f,Floor+1,13.9f),90,isolation);

            var infected = Place("Assets/Prefabs/Blender/Enemies/Zombie_HeadBanger.prefab",-4.5f,Floor,12.1f,175);
            var spawner = infected.GetComponent<InfectedSpawner>();
            spawner.bangHeadOnWall = false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(spawner);
            Threshold("Enter hall", infected, 0,0,2.2f,3.2f,4);
            var head = Place("Assets/Prefabs/Enemies/CryingHead.prefab",4.85f,Floor+.22f,12.35f,180);
            Threshold("Enter gallery",head,3.6f,Floor+1,7.4f,2.8f,2.4f);
        }

        Valve Wheel(float x,float z,float yaw)
        {
            var wheel = Place(ValveBuilder.PrefabPath,x,Floor+1.2f,z,yaw).GetComponent<Valve>();
            wheel.turnTime = 3.5f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(wheel);
            return wheel;
        }

        void Jet(string name,Vector3 position,float yaw,Valve wheel)
        {
            var jet = new GameObject(name).AddComponent<SteamLeak>();
            jet.transform.SetParent(root,false);
            jet.transform.localPosition = position;
            jet.transform.localRotation = Quaternion.Euler(0,yaw,0);
            jet.valve = wheel;
            jet.material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/Steam.mat");
            jet.length = 2.4f;
            jet.speed = 3f;
            jet.endRadius = .72f;
            var blocker = jet.gameObject.AddComponent<BoxCollider>();
            blocker.center = new Vector3(0,.85f,1.2f);
            blocker.size = new Vector3(.24f,3.7f,2.42f);
            jet.passageBlocker = blocker;
            var nozzle = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            nozzle.name = "Burst pipe";
            nozzle.transform.SetParent(jet.transform,false);
            nozzle.transform.localRotation = Quaternion.Euler(90,0,0);
            nozzle.transform.localScale = new Vector3(.25f,.18f,.25f);
            nozzle.GetComponent<Renderer>().sharedMaterial = iron;
        }

        void Threshold(string name,GameObject encounter,float x,float y,float z,float width,float depth)
        {
            encounter.SetActive(false);
            PrefabUtility.RecordPrefabInstancePropertyModifications(encounter);
            var trigger = new GameObject(name).AddComponent<BoxCollider>();
            trigger.transform.SetParent(root,false);
            trigger.transform.localPosition = new Vector3(x,y,z);
            trigger.size = new Vector3(width,4,depth);
            trigger.isTrigger = true;
            trigger.gameObject.AddComponent<EncounterThreshold>().encounter = encounter;
        }
    }
}
