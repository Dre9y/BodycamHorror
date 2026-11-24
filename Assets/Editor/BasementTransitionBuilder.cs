using System.Linq;
using BodycamHorror.Enemies;
using BodycamHorror.Environment;
using BodycamHorror.Weapons;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace BodycamHorror.EditorTools
{
    internal sealed class BasementTransitionBuilder
    {
        const string RootName = "BasementTransition";
        const string Materials = "Assets/Generated/Materials/Basement/Basement_";
        const float Height = 3;
        const float ApproachStart = 9.4f;
        const float ApproachLength = 29.2f;
        const float JunctionZ = ApproachStart + ApproachLength;
        const float ScareLead = 10f;
        const float RoomStart = JunctionZ + 2.6f;
        const float StairStart = RoomStart + 9.4f;
        const float WorkbenchZ = StairStart - 2.1f;

        readonly Transform root;
        readonly Material brick = Material("BrickPlaster"), floor = Material("FloorConcrete"), ceiling = Material("Ceiling");

        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat");

        BasementTransitionBuilder(Transform landing)
        {
            root = new GameObject(RootName).transform;
            root.SetParent(landing, false);
            root.localPosition = new Vector3(2.25f, 0, 19.2f);
        }

        [MenuItem("BodycamHorror/Level/Rebuild Basement Transition")]
        static void Rebuild()
        {
            var landing = GameObject.Find("PvE_Landing");
            if (!landing || landing.scene.path != "Assets/Scenes/Level.unity") return;
            var previous = landing.transform.Find(RootName);
            Build(landing.transform);
            if (previous) Undo.DestroyObjectImmediate(previous.gameObject);
            Undo.RegisterCreatedObjectUndo(landing.transform.Find(RootName).gameObject, "Rebuild basement transition");
            EditorSceneManager.MarkSceneDirty(landing.scene);
        }

        internal static void Build(Transform landing)
        {
            var b = new BasementTransitionBuilder(landing);
            try
            {
                b.Layout();
                b.Collapse();
                ImportScene("TestRange", b.PlacePuzzleAndPickup);
                ImportScene("JunctionScare", b.PlaceCrossing);
            }
            catch
            {
                Object.DestroyImmediate(b.root.gameObject);
                throw;
            }
        }

        static void ImportScene(string name, System.Action<Scene> copy)
        {
            string path = "Assets/Scenes/" + name + ".unity";
            var scene = SceneManager.GetSceneByPath(path);
            bool opened = !scene.isLoaded;
            if (opened) scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
            try { copy(scene); }
            finally { if (opened) EditorSceneManager.CloseScene(scene, true); }
        }

        GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material mat)
        {
            var a = root.TransformPoint(x0,y0,z0);
            var b = root.TransformPoint(x1,y1,z1);
            var go = LightingTestSceneBuilder.Box(name, root, Vector3.Min(a,b), Vector3.Max(a,b), mat, 2, 2);
            go.transform.rotation = Quaternion.identity;
            return go;
        }

        void Shell(string name, float x0, float x1, float z0, float z1)
        {
            Box(name + " floor",x0,x1,-.2f,0,z0,z1,floor);
            Box(name + " ceiling",x0,x1,Height,Height+.2f,z0,z1,ceiling);
        }

        void Across(string name, float z, float x0, float x1) => Box(name,x0,x1,0,Height,z-.1f,z+.1f,brick);
        void Along(string name, float x, float z0, float z1) => Box(name,x-.1f,x+.1f,0,Height,z0,z1,brick);

        GameObject Prop(string name,float x,float y,float z,float yaw=0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Basement/"+name+".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
            go.transform.localPosition = new Vector3(x,y,z);
            go.transform.localRotation = Quaternion.Euler(0,yaw,0);
            return go;
        }

        void Lamp(float x,float z,Color color)
        {
            var l = Prop("Lamp_Caged",x,Height,z).GetComponentInChildren<Light>();
            l.color=color;
            l.range=7;
            l.shadows=LightShadows.None;
            PrefabUtility.RecordPrefabInstancePropertyModifications(l);
        }

        void Layout()
        {
            Shell("Entry",-1.6f,1.6f,0,6);
            Along("Entry west",-1.6f,0,6);
            Along("Entry east",1.6f,0,6);
            Shell("Ring",-1.6f,11,6,17.6f);
            Across("Ring south",6,1.6f,11);
            Across("Ring north",17.6f,-1.6f,11);
            Along("Ring west",-1.6f,6,17.6f);
            Along("Outlet jamb",11,6,7);
            Along("Ring east",11,9.4f,17.6f);
            Box("Central masonry core",1.6f,7.7f,0,Height,9.4f,14.2f,brick);

            Shell("Steam passage",11,21.2f,7,9.4f);
            Across("Steam passage south",7,11,21.2f);
            Across("Steam passage north",9.4f,11,18.8f);
            Along("Elbow east",21.2f,7,JunctionZ);
            Shell("Approach",18.8f,21.2f,ApproachStart,JunctionZ);
            Along("Approach west",18.8f,ApproachStart,JunctionZ);

            // North of the donor crossing. Its north wall has an opening at x=3..5.
            Shell("Flashlight room",2,11,RoomStart,StairStart);
            Along("Flashlight room west",2,RoomStart,StairStart);
            Across("Flashlight room north",StairStart,2,11);
            Along("Shared wall",11,RoomStart,StairStart-2.4f);
            Along("Shared jamb",11,StairStart-.7f,StairStart);
            Shell("Future encounter",11,21.2f,RoomStart,StairStart);
            Along("Future encounter east",21.2f,RoomStart,StairStart);
            Along("Partition",14.2f,RoomStart+2.1f,StairStart);
            Across("Stair return",StairStart,11,17.4f);
            Across("Stair return",StairStart,19.8f,21.2f);
            for (int i=0;i<16;i++)
                Box("Basement tread " + (i+1),17.4f,19.8f,-3,-.17f*(i+1),StairStart+i*.32f,StairStart+(i+1)*.32f,floor);
            Box("Basement landing",17.4f,19.8f,-3,-2.72f,StairStart+5.12f,StairStart+7.2f,floor);
            Box("Stairwell west",17.2f,17.4f,-3,Height,StairStart,StairStart+7.4f,brick);
            Box("Stairwell east",19.8f,20,-3,Height,StairStart,StairStart+7.4f,brick);
            Box("Stairwell ceiling",17.2f,20,Height,Height+.2f,StairStart,StairStart+7.4f,ceiling);
            Box("Temporary basement end wall",17.4f,19.8f,-2.72f,Height,StairStart+7.2f,StairStart+7.4f,brick);
            var socket=new GameObject("BasementConnection").transform;
            socket.SetParent(root,false);
            socket.localPosition=new Vector3(18.6f,-2.72f,StairStart+7.2f);

            var warm=new Color(1,.78f,.53f);
            var cool=new Color(.64f,.79f,1);
            Lamp(.85f,4,warm);
            Lamp(.65f,16.1f,warm);
            Lamp(9.1f,10,cool);
            Lamp(14,8.2f,warm);
            Lamp(20,15,cool);
            Lamp(20,JunctionZ-9f,cool);
            Lamp(5,RoomStart+3.4f,warm);
            Lamp(17,RoomStart+2.4f,cool);
            Lamp(18.6f,StairStart+1f,cool);
        }

        void Collapse()
        {
            // A real low sill, tilted slabs and exposed reinforcement. The eye can see the
            // red wheel through the slit; even the prone character cannot fit between rods.
            Box("Collapsed wall footing",-1.6f,1.6f,0,.94f,11,11.9f,brick);
            for (int i=0;i<5;i++)
            {
                float x=-1.6f+.64f*i;
                var slab=Box("Broken masonry",x,x+.69f,.72f,1.23f+(i%2)*.19f,10.85f,11.89f,brick);
                slab.transform.Rotate(7-i*3,3*i-6,9-i*4);
                var pile=Prop("Brick_Pile",x+.31f,0,10.51f-(i%2)*.35f,45*i);
                pile.transform.localScale=Vector3.one*(1.2f+.1f*i);
            }
            Box("Fallen lintel",-1.6f,1.6f,2.32f,3,10.98f,11.96f,brick).transform.Rotate(0,0,5);
            var metal=Material("Rust");
            for(int i=0;i<9;i++)
            {
                float x=-1.31f+i*.36f;
                Box("Exposed rebar",x,x+.035f,.95f,2.65f,11.25f,11.29f,metal).transform.Rotate(0,0,i%2==0?5:-4);
            }
        }

        T FindIn<T>(Scene scene, System.Func<T,bool> matches) where T:Component =>
            scene.GetRootGameObjects().SelectMany(go=>go.GetComponentsInChildren<T>(true)).Single(matches);

        GameObject Copy(GameObject source,Vector3 position,Quaternion rotation)
        {
            var clone=Object.Instantiate(source,root,false);
            clone.name=source.name;
            clone.transform.localPosition=position;
            clone.transform.localRotation=rotation;
            // Generated meshes in donor scenes are scene-local subobjects.
            foreach(var filter in clone.GetComponentsInChildren<MeshFilter>(true))
            {
                var mesh=filter.sharedMesh;
                if(mesh && (AssetDatabase.GetAssetPath(mesh)=="" || AssetDatabase.GetAssetPath(mesh).EndsWith(".unity")))
                    filter.sharedMesh=Object.Instantiate(mesh);
            }
            return clone;
        }

        void PlacePuzzleAndPickup(Scene scene)
        {
            var source=FindIn<SteamLeak>(scene, leak=>leak.valve);
            var wheel=ValveBuilder.Place(root.TransformPoint(0,1.62f,17.49f),-root.forward,root).GetComponent<Valve>();
            var rim = wheel.wheel.GetComponent<MeshRenderer>();
            rim.sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/IsolationWheelRed.mat");
            PrefabUtility.RecordPrefabInstancePropertyModifications(rim);
            var leak=Copy(source.gameObject,new Vector3(12.05f,1.1f,9.14f),Quaternion.Euler(0,180,0)).GetComponent<SteamLeak>();
            // The breach sits on a pipe joint on the north wall. Its isolation valve is around the ring.
            Prop("Pipe_Large_2m",11.95f,1.1f,9.27f);
            Prop("Pipe_Large_2m",13.95f,1.1f,9.27f);
            leak.valve=wheel;
            var block=leak.gameObject.AddComponent<BoxCollider>();
            block.center=new Vector3(0,.4f,.94f);
            block.size=new Vector3(.22f,Height,2.55f);
            leak.passageBlocker=block;

            var torch=FindIn<WeaponPickup>(scene,p=>p.weapon==WeaponPickup.Kind.PistolLight);
            var bench=Prop("Workbench",4.3f,0,WorkbenchZ,90);
            var top=bench.GetComponentsInChildren<Collider>().Max(c=>c.bounds.max.y);
            Copy(torch.gameObject,new Vector3(4.3f,top-root.position.y,WorkbenchZ),Quaternion.Euler(0,80,0));
        }

        void PlaceCrossing(Scene scene)
        {
            // In JunctionScare the crossing begins at z=20.
            Vector3 offset=new Vector3(20,0,JunctionZ-20f);
            var source=FindIn<RunnerScare>(scene,s=>true);
            var crossing=FindIn<Transform>(scene,t=>t.name=="Crossing" && t.parent && t.parent.name=="Environment");
            var copy=Copy(crossing.gameObject,offset+crossing.position,crossing.rotation);
            // Retain the original wall and surface for the entire crawl, cutting a doorway
            // into its far left end (well beyond the last path point).
            foreach(Transform piece in copy.transform.Cast<Transform>().Where(t=>t.name.StartsWith("Wall_Far")).ToArray())
            {
                var box=piece.GetComponent<BoxCollider>();
                float y=piece.localPosition.y;
                float h=box.size.y*.5f;
                var mat=piece.GetComponent<MeshRenderer>().sharedMaterial;
                Box(piece.name,1.7f,3,y-h,y+h,RoomStart,RoomStart+.3f,mat);
                Box(piece.name,5,23.9f,y-h,y+h,RoomStart,RoomStart+.3f,mat);
                Object.DestroyImmediate(piece.gameObject);
            }
            var lamp=source.flickerLight.transform;
            while(lamp.parent && lamp.parent.name!="Lamps") lamp=lamp.parent;
            var light=Copy(lamp.gameObject,offset+lamp.position,lamp.rotation).GetComponentInChildren<Light>();
            // Give the player time to face the junction after turning past the steam.
            var scare=Copy(source.gameObject,new Vector3(20,1.5f,JunctionZ-ScareLead),source.transform.rotation).GetComponent<RunnerScare>();
            scare.flickerLight=light;
            scare.from=root.TransformPoint(offset+source.from);
            scare.to=root.TransformPoint(offset+source.to);
            for(int i=0;i<scare.path.Length;i++)
            {
                scare.path[i]=root.TransformPoint(offset+source.path[i]);
                scare.normals[i]=root.TransformDirection(source.normals[i]);
            }
        }
    }
}
