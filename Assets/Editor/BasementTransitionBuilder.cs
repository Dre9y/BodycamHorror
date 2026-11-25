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
        // One pipe runs from the isolation valve round the ring to the leak: following it is the puzzle.
        // The line hangs just over head height, close to the wall: low enough to be seen and followed.
        const float ValvePipeY = 1.62f, PipeY = 2.3f, LeakPipeY = 1.5f, PipeWall = .14f, PipeRadius = .09f;
        const float LeakX = 12.6f;

        readonly Transform root;
        // Walls and ceilings as everywhere else in the level: oil paint below the panel line, plaster above.
        // Brick is left only in the rubble, where the wall has broken open.
        readonly Material brick = Material("BrickPlaster"), floor = Level("Tiles110_Matte"), ceiling;
        // The scanned painted metal is mirror-glossy under the bulbs: matte copies for the pipe and the wheel.
        readonly Material pipeMat = Matte("PaintedMetal006","PaintedMetal006_Matte",.18f), wheelMat = Matte("IsolationWheelRed","IsolationWheelRed_Matte",.25f);

        static Material Material(string name) => AssetDatabase.LoadAssetAtPath<Material>(Materials + name + ".mat");
        // The level's own scanned materials, loaded as they are (LoadMaterials would also rewrite other materials).
        static Material Level(string name) => AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Environment/" + name + ".mat");
        static readonly Material paint = Level("PaintedPlaster011"), plaster = Level("PaintedPlaster015");

        static Material Matte(string source, string name, float smoothness)
        {
            string path = "Assets/Materials/Environment/" + name + ".mat";
            var original = Level(source);
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(original); AssetDatabase.CreateAsset(m, path); }
            m.CopyPropertiesFromMaterial(original);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_SpecularHighlights", 0);
            m.DisableKeyword("_SPECULARHIGHLIGHTS_OFF");
            m.EnableKeyword("_SPECULARHIGHLIGHTS_OFF");
            EditorUtility.SetDirty(m);
            return m;
        }

        BasementTransitionBuilder(Transform landing)
        {
            ceiling = Level("Plaster007");
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
            OpenVestibule(landing.transform);
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
                b.PipeRun();
                b.Mutants();
                b.Dress();
                b.EncounterStub();
                ImportScene("TestRange", b.PlacePuzzleAndPickup);
                ImportScene("JunctionScare", b.PlaceCrossing);
            }
            catch
            {
                Object.DestroyImmediate(b.root.gameObject);
                throw;
            }
        }

        /// <summary>An older landing build closed the vestibule behind the iron door with a wall and the
        /// bottom of a flight up. The way on is the basement now: take them out if the scene still has them.</summary>
        static void OpenVestibule(Transform landing)
        {
            var vestibule = landing.Find("Vestibule");
            if (!vestibule) return;
            var root = landing.Find(RootName);
            foreach (Transform piece in vestibule.Cast<Transform>().ToArray())
            {
                var r = piece.GetComponent<Renderer>();
                if (!r) continue;
                Vector3 a = root.InverseTransformPoint(r.bounds.min), b = root.InverseTransformPoint(r.bounds.max);
                bool endWall = Mathf.Abs(a.x - b.x) > 3f && Mathf.Abs(a.z - b.z) < .5f && Mathf.Abs(a.y - b.y) > .5f;
                if (piece.name.StartsWith("StepUp") || endWall) Undo.DestroyObjectImmediate(piece.gameObject);
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

        GameObject Box(string name, float x0, float x1, float y0, float y1, float z0, float z1, Material mat, float tile = 2)
        {
            var a = root.TransformPoint(x0,y0,z0);
            var b = root.TransformPoint(x1,y1,z1);
            var go = LightingTestSceneBuilder.Box(name, root, Vector3.Min(a,b), Vector3.Max(a,b), mat, tile, tile);
            go.transform.rotation = Quaternion.identity;
            return go;
        }

        void Shell(string name, float x0, float x1, float z0, float z1)
        {
            Box(name + " floor",x0,x1,-.2f,0,z0,z1,floor);
            Box(name + " ceiling",x0,x1,Height,Height+.2f,z0,z1,ceiling);
        }

        void Wall(string name, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            const float panel = LevelSceneBuilder.PanelHeight, tile = LightingTestSceneBuilder.WallTile;
            if (y0 < panel) Box(name + " paint",x0,x1,y0,Mathf.Min(y1,panel),z0,z1,paint,tile);
            if (y1 > panel) Box(name + " plaster",x0,x1,Mathf.Max(y0,panel),y1,z0,z1,plaster,tile);
        }

        void Across(string name, float z, float x0, float x1) => Wall(name,x0,x1,0,Height,z-.1f,z+.1f);
        void Along(string name, float x, float z0, float z1) => Wall(name,x-.1f,x+.1f,0,Height,z0,z1);

        GameObject Prop(string name,float x,float y,float z,float yaw=0)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Basement/"+name+".prefab");
            var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
            go.transform.localPosition = new Vector3(x,y,z);
            go.transform.localRotation = Quaternion.Euler(0,yaw,0);
            return go;
        }

        void Emergency(float x,float z,float y=Height-.1f)
        {
            var lamp=new GameObject("Emergency lamp").transform;
            lamp.SetParent(root,false);
            lamp.localPosition=new Vector3(x,y,z);
            var fixture=GameObject.CreatePrimitive(PrimitiveType.Cube);
            fixture.name="Fixture";
            Object.DestroyImmediate(fixture.GetComponent<Collider>());
            fixture.transform.SetParent(lamp,false);
            fixture.transform.localScale=new Vector3(.3f,.08f,.14f);
            fixture.GetComponent<MeshRenderer>().sharedMaterial=Level("LightFear_EmergencyLamp");
            var light=new GameObject("Light").AddComponent<Light>();
            light.transform.SetParent(lamp,false);
            light.transform.localPosition=Vector3.down*.15f;
            light.type=LightType.Point;
            light.color=new Color(1,.1f,.05f);
            light.intensity=1.6f;
            light.range=7;
            light.shadows=LightShadows.None;
        }

        void Lamp(float x,float z,Color color,float range=7)
        {
            var l = Prop("Lamp_Caged",x,Height,z).GetComponentInChildren<Light>();
            l.color=color;
            l.range=range;
            l.shadows=LightShadows.None;
            PrefabUtility.RecordPrefabInstancePropertyModifications(l);
        }

        void Layout()
        {
            Shell("Entry",-1.6f,1.6f,0,6);
            // The vestibule's ceiling is lower (2.7): close the step up to this one.
            Wall("Entry header",-1.6f,1.6f,2.7f,Height,-.05f,.1f);
            Along("Entry west",-1.6f,0,6);
            Along("Entry east",1.6f,0,6);
            Shell("Ring",-1.6f,11,6,17.6f);
            Across("Ring south",6,1.6f,11);
            Across("Ring north",17.6f,-1.6f,2);   // the valve's wall; east of it the ring opens into the horseshoe
            Along("Ring west",-1.6f,6,17.6f);
            Along("Outlet jamb",11,6,7);
            Along("Ring east",11,9.4f,17.6f);
            Wall("Central core",1.6f,7.7f,0,Height,9.4f,14.2f);

            // The horseshoe north of the ring. Its inner block also closes the ring's north arm, so from the
            // steam the way round to the valve is up the east leg, across the top and down the west leg.
            Wall("Horseshoe inner block",4.7f,7.7f,0,Height,14.2f,24.1f);
            Shell("Horseshoe west",1.8f,4.7f,17.6f,27.1f);
            Shell("Horseshoe top",4.7f,7.7f,24.1f,27.1f);
            Shell("Horseshoe east",7.7f,11.1f,17.6f,27.1f);
            Along("Horseshoe west",1.9f,17.6f,27.1f);
            Across("Horseshoe north",27,1.8f,11.1f);
            Along("Horseshoe east",11,17.6f,27.1f);

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
            Wall("Stairwell west",17.2f,17.4f,-3,Height,StairStart,StairStart+7.4f);
            Wall("Stairwell east",19.8f,20,-3,Height,StairStart,StairStart+7.4f);
            Box("Stairwell ceiling",17.2f,20,Height,Height+.2f,StairStart,StairStart+7.4f,ceiling);
            Wall("Temporary basement end wall",17.4f,19.8f,-2.72f,Height,StairStart+7.2f,StairStart+7.4f);
            var socket=new GameObject("BasementConnection").transform;
            socket.SetParent(root,false);
            socket.localPosition=new Vector3(18.6f,-2.72f,StairStart+7.2f);

            var warm=new Color(1,.78f,.53f);
            var cool=new Color(.64f,.79f,1);
            Lamp(.85f,4,warm);
            Lamp(0,16.6f,warm);   // right over the wheel: the brightest thing seen through the rubble
            Lamp(9.1f,10,cool);
            Lamp(9.3f,15.9f,cool,6);   // the way round to the wheel must not be pitch black: no flashlight yet
            Lamp(9.3f,21.5f,cool,5);
            Lamp(5f,RoomStart+3.4f+4f,warm,6);
            Lamp(20,22.5f,cool,6);
            // Red emergency lamps, as in the basement level below: over the mutant's corner, halfway down the
            // long approach, over the encounter room and the stairs down.
            Emergency(3.4f,26.2f);
            Emergency(20,33.5f);
            Emergency(13,49.8f);
            Emergency(18.6f,StairStart+3.6f);
            Emergency(18.6f,StairStart+6.9f,-.5f);   // down on the landing: the stairs end in a red glow
            Lamp(14,8.2f,warm);
            Lamp(20,15,cool);
            Lamp(20,JunctionZ-9f,cool);
            Lamp(5,RoomStart+3.4f,warm);
            // The left arm of the T ends under a lamp: the doorway to the flashlight is a lit frame from the junction.
            Lamp(3.6f,JunctionZ+1.1f,cool,4.5f);
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
            // The player sees the wheel through the slit; the monsters must not see the player through it
            // (they would only press against the rubble). An unrendered solid pane fills the slit: their sight
            // rays stop on it, and nothing can be shot or squeezed through anyway.
            var pane=new GameObject("Rubble sight blocker");
            pane.transform.SetParent(root,false);
            pane.transform.localPosition=new Vector3(0,Height*.5f,11.4f);
            pane.AddComponent<BoxCollider>().size=new Vector3(3.2f,Height,.4f);
            var metal=Material("Rust");
            // Spaced so the wheel (x=0) shows in the gap between two rods.
            for(int i=0;i<8;i++)
            {
                float x=-1.26f+i*.36f-.0175f;
                Box("Exposed rebar",x,x+.035f,.95f,2.65f,11.25f,11.29f,metal).transform.Rotate(0,0,i%2==0?5:-4);
            }
        }

        GameObject Pipe(Vector3 a,Vector3 b)
        {
            var go=Prop("Pipe_Small_2m",0,0,0);
            go.name="Valve line";
            float half=Vector3.Distance(a,b)*.5f;
            go.transform.localPosition=(a+b)*.5f;
            go.transform.localRotation=Quaternion.FromToRotation(Vector3.right,b-a);
            go.transform.localScale=new Vector3(half,2.05f,2.05f);
            foreach(var r in go.GetComponentsInChildren<Renderer>())
            {
                r.sharedMaterial=pipeMat;
                PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            // Solid: nobody walks through it. In the scaled space: radius .09 m, the full length.
            var col=go.AddComponent<CapsuleCollider>();
            col.direction=0;
            col.radius=.046f;
            col.height=2+2*PipeRadius/half;
            return go;
        }

        // A cast fitting at a corner: a ball a little fatter than the pipe, so any turn (up, along) fits.
        void Fitting(Vector3 at)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name="Valve line fitting";
            go.transform.SetParent(root,false);
            go.transform.localPosition=at;
            go.transform.localScale=Vector3.one*(PipeRadius*2.6f);
            go.GetComponent<Renderer>().sharedMaterial=pipeMat;
        }

        /// <summary>Pipe through the points with a fitting at each inner one; the first and last are its ends.</summary>
        void PipeLine(params Vector3[] points)
        {
            for(int i=0;i<points.Length-1;i++)
            {
                Pipe(points[i],points[i+1]);
                if(i+1<points.Length-1) Fitting(points[i+1]);
            }
        }

        void PipeRun()
        {
            // From the end of the valve's own pipe (x 1.6 on the ring's north wall, at hand height) it rises
            // just over head height and runs the way the player has to go: round the horseshoe, down the ring's
            // east wall and along the steam passage, where it has burst.
            float valveZ=17.5f-ValveBuilder.WallOffset, west=2+PipeWall, top=26.9f-PipeWall, east=10.9f-PipeWall, passage=9.3f-PipeWall;
            PipeLine(
                new Vector3(1.6f,ValvePipeY,valveZ),
                new Vector3(west,ValvePipeY,valveZ),
                new Vector3(west,PipeY,valveZ),
                new Vector3(west,PipeY,top),
                new Vector3(east,PipeY,top),
                new Vector3(east,PipeY,passage),
                // into the steam passage it drops to chest height: the burst is right in front of the face
                new Vector3(east,LeakPipeY,passage),
                // before the turn into the long approach it goes up into the ceiling: at chest height it would bar the turn
                new Vector3(18.5f,LeakPipeY,passage),
                new Vector3(18.5f,Height,passage));
        }

        GameObject Put(string prefab,float x,float z,float yaw=0,float y=0)
        {
            var go=Prop(prefab,x,y,z,yaw);
            go.transform.SetParent(dressing,true);
            return go;
        }

        Transform dressing;

        /// <summary>Junk along the walls, the way kept clear: a lived-in, abandoned basement.
        /// Prefabs stand on their pivot with the front to +Z; yaw turns the front into the room.</summary>
        void Dress()
        {
            dressing=new GameObject("Dressing").transform;
            dressing.SetParent(root,false);
            // entry
            Put("Fusebox",-1.5f,2.2f,90,.6f);
            Put("Trash_Newspaper",1.2f,1.5f,40);
            Put("Trash_Cardboard",-1.0f,4.6f,30);
            // ring, west arm, up against the rubble
            Put("Bucket",1.25f,9.9f);
            Put("PaintCan_A",1.3f,9.4f,70);
            Put("Brick_Half",-.4f,9.6f,20);
            Put("Brick_Half",.3f,9.9f,75);
            // ring, south arm
            Put("Pallet",3.5f,6.55f);
            Put("Crate",3.5f,6.5f,8,.15f);
            Put("Barrel",6.0f,6.45f);
            Put("Barrel_Open",6.85f,6.5f,40);
            Put("Trash_Debris",8.6f,8.7f,15);
            // ring, east arm and the corner to the horseshoe
            Put("Shelf_Metal",8.02f,12f,90);
            Put("PaintCan_B",8.35f,13.4f,10);
            Put("Tyre",10.3f,15f);
            Put("Planks_Pile",10.5f,16.4f,90);
            // by the valve
            Put("Chair_Broken",-1f,15f,120);
            Put("Trash_Rag",1f,15.5f,20);
            // horseshoe
            Put("CementBags",10.35f,19.5f);
            Put("Barrel_Open",10.5f,23.2f,110);
            Put("Cabinet_Metal",8.6f,26.62f,180);
            Put("Crate_Open",6.2f,24.65f,20);
            Put("Mattress",2.45f,22.2f,90);   // where the west-leg one sleeps
            Put("Trash_Debris",4.05f,21f,50);
            Put("Trash_BottleA",2.6f,20.2f);
            Put("Trash_TinCan_A",3.9f,24.9f,30);
            // steam passage, out of the jet
            Put("GasCylinder",15.2f,7.35f);
            Put("GasCylinder",15.55f,7.32f,60);
            Put("PaintCan_A",17.5f,7.3f);
            // the long approach
            Put("Trash_Newspaper",20.6f,12.5f,10);
            Put("Radiator_Basement",19.0f,16f,90,.25f);
            Put("Floor_Drain",20f,22.5f);
            Put("Trash_Cardboard",19.45f,26f,80);
            Put("Chair_Broken",20.7f,35.5f,-100);
            // flashlight room
            Put("Shelf_Wood",2.4f,44f,90);
            Put("Crate",9.8f,42.2f);
            Put("Crate",9.85f,42.2f,12,.54f);
            Put("Barrel",10.4f,45.6f);
            Put("Trash_Rag",6.5f,45.5f,60);
            // encounter room: kept open for the fight
            Put("Pallet",20.4f,42.3f,90);

            Kit();
        }

        // ---- the Blender-made kit (Blender/tools/basement_dressing.py -> BasementDressingSetup).
        // Its wall pieces have the wall at local z=0 and face +Z; the ceiling pieces hang from local y=0.
        GameObject Kit(string name,Vector3 at,Quaternion rotation)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>(BasementDressingSetup.PrefabDir+"/"+name+".prefab");
            if(!prefab) return null;
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,dressing);
            go.transform.localPosition=at;
            go.transform.localRotation=rotation;
            return go;
        }

        GameObject Kit(string name,float x,float y,float z,float yaw=0) => Kit(name,new Vector3(x,y,z),Quaternion.Euler(0,yaw,0));

        void Kit()
        {
            // cables clipped to the ceiling: down the whole long approach, along the ring's south arm
            for(float z=11;z<38;z+=2) Kit("Cable_Run_2m",20.75f,Height,z,90);
            for(float x=3;x<11;x+=2) Kit("Cable_Run_2m",x,Height,6.35f);
            // torn ones hanging down: by the wall in the T's left arm, in the mutant's corner, in the encounter room
            Kit("Cable_Torn",12.5f,Height,40.85f,15);
            Kit("Cable_Torn",5.6f,Height,26.3f,-20);
            Kit("Cable_Torn",15.5f,Height,47,60);
            // a vent duct along the steam passage's south wall, its grille at the turn into the approach
            for(float x=12.2f;x<17;x+=2) Kit("Duct_2m",x,Height,7.45f);
            Kit("Duct_2m_Grille",18.2f,Height,7.45f,180);
            // the residents' storage cages along the flashlight room's north wall
            Kit("Storage_Lockers",7.3f,0,50.5f-.85f,180);
            // the signs: the heating inlet over the valve (a hint), "basement" at the way in
            Kit("Sign_Heating",-.95f,2.2f,17.49f,180);
            Kit("Sign_Basement",-1.49f,1.9f,.9f,90);
            // handrails down both sides of the stairs
            float run=16*.32f, drop=2.72f, mid=StairStart+run*.5f;
            Vector3 d=new Vector3(0,-drop,run).normalized;
            foreach(var (x,n) in new[]{(17.4f,Vector3.right),(19.8f,Vector3.left)})
                Kit("Stair_Handrail_6m",new Vector3(x,-drop*.5f+.9f,mid),Quaternion.LookRotation(n,Vector3.Cross(d,n)));
            // the residents' things
            Kit("Sled",10.45f,0,21f,90);
            Kit("Bicycle",new Vector3(20.62f,0,19f),Quaternion.Euler(0,90,0)*Quaternion.Euler(8,0,0));
            Kit("Jars_Preserves",3.5f,.69f,6.45f,8);
            Kit("Jars_Preserves",8.6f,1.87f,26.6f,180);
        }

        void Mutants()
        {
            // The red marks on the plan: one round the top corner, one halfway down the west leg, both turned
            // away from where the player comes from. They stand until they see the player.
            Mutant("Mutant_TopCorner",3.3f,25.7f,-60);
            Mutant("Mutant_WestLeg",3.5f,23.3f,170);   // deep in the leg: not in line with the rubble slit
        }

        void Mutant(string name,float x,float z,float yaw)
        {
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Blender/Enemies/Zombie_HeadBanger.prefab");
            var go=(GameObject)PrefabUtility.InstantiatePrefab(prefab,root);
            go.name=name;
            go.transform.localPosition=new Vector3(x,0,z);
            go.transform.localRotation=Quaternion.Euler(0,yaw,0);
            var s=go.GetComponent<InfectedSpawner>();
            s.count=1;
            s.radius=0;
            s.bangHeadOnWall=false;
            s.bangHearZone=default(Bounds);
            s.faceForward=true;
            s.roamerShare=0;
            s.startAlerted=false;
            s.respawnWhenAllDead=false;
            PrefabUtility.RecordPrefabInstancePropertyModifications(s);
        }

        void EncounterStub()
        {
            // Placeholder: the fight and the riddle before the stairs are not designed yet.
            var stub=new GameObject("EncounterStub (fight + puzzle TODO)");
            stub.transform.SetParent(root,false);
            stub.transform.localPosition=new Vector3(16.1f,Height*.5f,RoomStart+4.7f);
            var zone=stub.AddComponent<BoxCollider>();
            zone.isTrigger=true;
            zone.size=new Vector3(10,Height,9.4f);
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
            rim.sharedMaterial = wheelMat;
            PrefabUtility.RecordPrefabInstancePropertyModifications(rim);
            // A big wheel: it has to read through the rubble from the far side of the ring.
            wheel.wheel.localScale *= 1.6f;
            PrefabUtility.RecordPrefabInstancePropertyModifications(wheel.wheel);
            foreach (var r in wheel.GetComponentsInChildren<MeshRenderer>())
                if (r != rim) { r.sharedMaterial = pipeMat; PrefabUtility.RecordPrefabInstancePropertyModifications(r); }
            // The breach is on the same pipe as the wheel, at face height across the passage.
            // The pipe is overhead here: the jet blows down and across the passage, through head height.
            float pipeZ=9.3f-PipeWall;
            var leak=Copy(source.gameObject,new Vector3(LeakX,LeakPipeY-.03f,pipeZ-.1f),Quaternion.Euler(8,180,0)).GetComponent<SteamLeak>();
            leak.valve=wheel;
            var curtain=new GameObject("Steam curtain").transform;
            curtain.SetParent(root,false);
            curtain.localPosition=new Vector3(LeakX,Height*.5f,(7.1f+9.3f)*.5f);
            var block=curtain.gameObject.AddComponent<BoxCollider>();
            block.size=new Vector3(.3f,Height,2.3f);
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
