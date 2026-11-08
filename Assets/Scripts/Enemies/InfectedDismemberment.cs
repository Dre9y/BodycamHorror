using System.Collections.Generic;
using BodycamHorror.Combat;
using BodycamHorror.Weapons;
using UnityEngine;
using UnityEngine.Rendering;

namespace BodycamHorror.Enemies
{
    /// <summary>
    /// Dynamic dismemberment of a skinned body. The first cut gives the body its own copy of the mesh;
    /// each cut then sorts triangles by skin weights into "stays" and "comes off", caps the wound on both
    /// sides along the real cut edge, bakes the current pose of the severed part into a loose physics
    /// piece, and leaves the stump bleeding. Parts can be taken off in steps (forearm, then upper arm).
    /// </summary>
    public class InfectedDismemberment : MonoBehaviour
    {
        const int MaxLoosePieces = 40;

        public float stumpBleedTime = 1.4f;
        public float pieceLifetime = 40f;
        public Color bloodColor = new Color(0.32f, 0.04f, 0.03f, 0.9f);

        static readonly Dictionary<Mesh, int[]> WeldCache = new Dictionary<Mesh, int[]>();
        static readonly Queue<GameObject> LoosePieces = new Queue<GameObject>();
        static Material fleshMaterial;

        struct Bleeder { public Transform bone, parent; public float until, next; }

        SkinnedMeshRenderer smr;
        Animator animator;
        InfectedRagdoll ragdoll;
        Rigidbody rootBody;
        Transform[] bones;
        Mesh mesh, bake;
        Material skinMaterial;

        List<Vector3> verts, normals;
        List<Vector4> tangents;
        List<Vector2> uvs;
        List<BoneWeight> weights;
        List<int> weld, bodyTris, capTris;
        int nextWeld;

        readonly HashSet<HumanBodyBones> severed = new HashSet<HumanBodyBones>();
        readonly List<Bleeder> bleeders = new List<Bleeder>();

        public void Init(SkinnedMeshRenderer renderer, Animator anim, InfectedRagdoll doll)
        {
            smr = renderer;
            animator = anim;
            ragdoll = doll;
            rootBody = GetComponent<Rigidbody>();
            bones = smr.bones;
        }

        public bool IsSevered(HumanBodyBones part) => severed.Contains(part);

        void OnDestroy()
        {
            if (mesh) Destroy(mesh);
            if (bake) Destroy(bake);
        }

        void Update()
        {
            for (int i = bleeders.Count - 1; i >= 0; i--)
            {
                Bleeder b = bleeders[i];
                if (Time.time >= b.until || !b.bone) { bleeders.RemoveAt(i); continue; }
                if (Time.time < b.next) continue;
                b.next = Time.time + 0.06f;
                bleeders[i] = b;
                Vector3 dir = b.parent ? (b.bone.position - b.parent.position).normalized : Vector3.up;
                ImpactEffects.Get().BloodDefault(b.bone.position, dir, bloodColor, 0.3f);
            }
        }

        /// <summary>Cuts off <paramref name="part"/> and everything below it. False if there is nothing left to cut.</summary>
        public bool Sever(HumanBodyBones part, Vector3 throwDirection, float throwSpeed)
        {
            if (!smr || severed.Contains(part)) return false;
            Transform bone = animator.GetBoneTransform(part);
            if (!bone || !smr.sharedMesh || !smr.sharedMesh.isReadable) return false;
            EnsureOwnMesh();

            // which bones and vertices come off
            var boneOff = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) boneOff[i] = bones[i] && bones[i].IsChildOf(bone);
            var vertOff = new bool[verts.Count];
            for (int i = 0; i < verts.Count; i++) vertOff[i] = WeightOn(weights[i], boneOff) > 0.5f;

            Split(bodyTris, vertOff, out List<int> keepBody, out List<int> offBody);
            if (offBody.Count < 30) return false;
            Split(capTris, vertOff, out List<int> keepCap, out List<int> offCap);

            List<List<(int a, int b)>> loops = FindCutLoops(keepBody, offBody);

            if (!bake) { bake = new Mesh(); bake.MarkDynamic(); }
            smr.BakeMesh(bake, true);   // current pose, before the mesh changes
            SpawnPiece(part, bone, offBody, offCap, loops, throwDirection, throwSpeed);

            // seal the wound on the body
            Vector3 offCentre = Centroid(offBody);
            int parentBone = NearestBoneIndex(bone.parent);
            foreach (var loop in loops) AddBodyCap(loop, offCentre, parentBone, keepCap);
            bodyTris = keepBody;
            capTris = keepCap;
            UploadMesh();

            severed.Add(part);
            foreach (var kv in Severable) if (kv != part && animator.GetBoneTransform(kv) && animator.GetBoneTransform(kv).IsChildOf(bone)) severed.Add(kv);
            if (ragdoll) ragdoll.DisableBelow(bone);
            bleeders.Add(new Bleeder { bone = bone, parent = bone.parent, until = Time.time + stumpBleedTime });
            ImpactEffects.Get().BloodDefault(bone.position, throwDirection, bloodColor, 2.5f);
            return true;
        }

        public static readonly HumanBodyBones[] Severable =
        {
            HumanBodyBones.Head,
            HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
            HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
        };

        // ------------------------------------------------------------------ mesh bookkeeping

        void EnsureOwnMesh()
        {
            if (mesh) return;
            Mesh source = smr.sharedMesh;
            mesh = Instantiate(source);
            mesh.name = source.name + " (cut)";
            mesh.MarkDynamic();

            verts = new List<Vector3>(); normals = new List<Vector3>(); tangents = new List<Vector4>(); uvs = new List<Vector2>();
            source.GetVertices(verts); source.GetNormals(normals); source.GetTangents(tangents); source.GetUVs(0, uvs);
            weights = new List<BoneWeight>(source.boneWeights);
            bodyTris = new List<int>(source.triangles);
            capTris = new List<int>();

            int[] w = WeldFor(source);
            weld = new List<int>(w);
            nextWeld = verts.Count + 1;

            skinMaterial = smr.sharedMaterial;
            smr.sharedMesh = mesh;
            smr.sharedMaterials = new[] { skinMaterial, FleshMaterial() };
        }

        /// <summary>Vertices split along UV seams share a position; cut edges are matched by this id.</summary>
        static int[] WeldFor(Mesh source)
        {
            if (WeldCache.TryGetValue(source, out int[] cached)) return cached;
            var v = source.vertices;
            var result = new int[v.Length];
            var map = new Dictionary<Vector3Int, int>(v.Length);
            for (int i = 0; i < v.Length; i++)
            {
                var key = new Vector3Int(Mathf.RoundToInt(v[i].x * 20000f), Mathf.RoundToInt(v[i].y * 20000f), Mathf.RoundToInt(v[i].z * 20000f));
                if (!map.TryGetValue(key, out int id)) { id = i; map[key] = id; }
                result[i] = id;
            }
            WeldCache[source] = result;
            return result;
        }

        static float WeightOn(BoneWeight w, bool[] off)
        {
            float s = 0f;
            if (off[w.boneIndex0]) s += w.weight0;
            if (off[w.boneIndex1]) s += w.weight1;
            if (off[w.boneIndex2]) s += w.weight2;
            if (off[w.boneIndex3]) s += w.weight3;
            return s;
        }

        static void Split(List<int> tris, bool[] vertOff, out List<int> keep, out List<int> off)
        {
            keep = new List<int>(tris.Count);
            off = new List<int>();
            for (int i = 0; i < tris.Count; i += 3)
            {
                int a = tris[i], b = tris[i + 1], c = tris[i + 2];
                int votes = (vertOff[a] ? 1 : 0) + (vertOff[b] ? 1 : 0) + (vertOff[c] ? 1 : 0);
                var into = votes >= 2 ? off : keep;
                into.Add(a); into.Add(b); into.Add(c);
            }
        }

        static long EdgeKey(int a, int b) => ((long)a << 32) | (uint)b;

        /// <summary>Edges of the part coming off that are shared with the part staying, grouped into closed cuts.</summary>
        List<List<(int a, int b)>> FindCutLoops(List<int> keep, List<int> off)
        {
            var keepEdges = new HashSet<long>();
            for (int i = 0; i < keep.Count; i += 3)
            {
                int a = weld[keep[i]], b = weld[keep[i + 1]], c = weld[keep[i + 2]];
                keepEdges.Add(EdgeKey(a, b)); keepEdges.Add(EdgeKey(b, c)); keepEdges.Add(EdgeKey(c, a));
            }

            var cut = new List<(int a, int b)>();
            void Check(int u, int v) { if (keepEdges.Contains(EdgeKey(weld[v], weld[u]))) cut.Add((u, v)); }
            for (int i = 0; i < off.Count; i += 3)
            {
                Check(off[i], off[i + 1]); Check(off[i + 1], off[i + 2]); Check(off[i + 2], off[i]);
            }

            // connected groups of cut edges
            var parent = new Dictionary<int, int>();
            int Find(int x)
            {
                if (!parent.TryGetValue(x, out int p)) { parent[x] = x; return x; }
                if (p == x) return x;
                int r = Find(p);
                parent[x] = r;
                return r;
            }
            foreach (var e in cut) parent[Find(weld[e.a])] = Find(weld[e.b]);

            var groups = new Dictionary<int, List<(int a, int b)>>();
            foreach (var e in cut)
            {
                int r = Find(weld[e.a]);
                if (!groups.TryGetValue(r, out var list)) groups[r] = list = new List<(int a, int b)>();
                list.Add(e);
            }
            var loops = new List<List<(int a, int b)>>();
            foreach (var g in groups.Values) if (g.Count >= 3) loops.Add(g);
            return loops;
        }

        Vector3 Centroid(List<int> tris)
        {
            Vector3 sum = Vector3.zero;
            for (int i = 0; i < tris.Count; i++) sum += verts[tris[i]];
            return sum / Mathf.Max(tris.Count, 1);
        }

        int NearestBoneIndex(Transform t)
        {
            for (; t; t = t.parent)
            {
                int i = System.Array.IndexOf(bones, t);
                if (i >= 0) return i;
            }
            return 0;
        }

        void AddBodyCap(List<(int a, int b)> loop, Vector3 offCentre, int parentBone, List<int> into)
        {
            Vector3 centre = Vector3.zero;
            foreach (var e in loop) centre += verts[e.a];
            centre /= loop.Count;
            Vector3 outward = (offCentre - centre).normalized;

            int centreIndex = AddVertex(centre, outward, new BoneWeight { boneIndex0 = parentBone, weight0 = 1f });
            var copies = new Dictionary<int, int>();
            int Copy(int v)
            {
                if (!copies.TryGetValue(v, out int c)) copies[v] = c = AddVertex(verts[v], outward, weights[v]);
                return c;
            }
            // winding: the cap takes over the edge exactly as the removed triangle had it
            foreach (var e in loop) { into.Add(Copy(e.a)); into.Add(Copy(e.b)); into.Add(centreIndex); }
        }

        int AddVertex(Vector3 position, Vector3 normal, BoneWeight weight)
        {
            verts.Add(position); normals.Add(normal); tangents.Add(new Vector4(1f, 0f, 0f, 1f)); uvs.Add(new Vector2(0.5f, 0.5f));
            weights.Add(weight);
            weld.Add(nextWeld++);
            return verts.Count - 1;
        }

        void UploadMesh()
        {
            mesh.SetTriangles(new int[0], 0, false);
            if (mesh.subMeshCount > 1) mesh.SetTriangles(new int[0], 1, false);
            mesh.SetVertices(verts);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uvs);
            mesh.boneWeights = weights.ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(bodyTris, 0, true);
            mesh.SetTriangles(capTris, 1, true);
        }

        // ------------------------------------------------------------------ the piece that comes off

        void SpawnPiece(HumanBodyBones part, Transform bone, List<int> offBody, List<int> offCap,
                        List<List<(int a, int b)>> loops, Vector3 throwDirection, float throwSpeed)
        {
            var bakedVerts = new List<Vector3>();
            var bakedNormals = new List<Vector3>();
            bake.GetVertices(bakedVerts);
            bake.GetNormals(bakedNormals);
            Transform t = smr.transform;

            // world-space geometry of the piece
            var outVerts = new List<Vector3>(); var outNormals = new List<Vector3>(); var outUv = new List<Vector2>(); var outTan = new List<Vector4>();
            var map = new Dictionary<int, int>();
            int Remap(int v)
            {
                if (map.TryGetValue(v, out int r)) return r;
                r = outVerts.Count;
                map[v] = r;
                outVerts.Add(t.TransformPoint(bakedVerts[v]));
                outNormals.Add(t.TransformDirection(bakedNormals[v]).normalized);
                outUv.Add(uvs[v]);
                outTan.Add(tangents[v]);
                return r;
            }
            var skin = new List<int>(offBody.Count);
            foreach (int v in offBody) skin.Add(Remap(v));
            var flesh = new List<int>(offCap.Count);
            foreach (int v in offCap) flesh.Add(Remap(v));
            if (outVerts.Count == 0) return;

            Vector3 centroid = Vector3.zero;
            foreach (var v in outVerts) centroid += v;
            centroid /= outVerts.Count;

            // close the piece along the same cut
            foreach (var loop in loops)
            {
                Vector3 c = Vector3.zero;
                foreach (var e in loop) c += t.TransformPoint(bakedVerts[e.a]);
                c /= loop.Count;
                Vector3 outward = (c - centroid).normalized;
                int centre = outVerts.Count;
                outVerts.Add(c); outNormals.Add(outward); outUv.Add(new Vector2(0.5f, 0.5f)); outTan.Add(new Vector4(1f, 0f, 0f, 1f));
                var copies = new Dictionary<int, int>();
                int Copy(int v)
                {
                    if (copies.TryGetValue(v, out int r)) return r;
                    r = outVerts.Count;
                    copies[v] = r;
                    outVerts.Add(t.TransformPoint(bakedVerts[v])); outNormals.Add(outward); outUv.Add(new Vector2(0.5f, 0.5f)); outTan.Add(new Vector4(1f, 0f, 0f, 1f));
                    return r;
                }
                foreach (var e in loop) { flesh.Add(Copy(e.b)); flesh.Add(Copy(e.a)); flesh.Add(centre); }
            }

            // the piece's own frame: Z along the limb, pivot at its middle
            Vector3 axis = centroid - bone.position;
            if (axis.sqrMagnitude < 1e-6f) axis = bone.up;
            Quaternion frame = Quaternion.LookRotation(axis.normalized, Mathf.Abs(Vector3.Dot(axis.normalized, Vector3.up)) > 0.95f ? Vector3.forward : Vector3.up);
            Quaternion toLocal = Quaternion.Inverse(frame);
            Vector3 min = Vector3.one * float.MaxValue, max = Vector3.one * float.MinValue;
            for (int i = 0; i < outVerts.Count; i++)
            {
                Vector3 local = toLocal * (outVerts[i] - centroid);
                outVerts[i] = local;
                outNormals[i] = toLocal * outNormals[i];
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }

            var m = new Mesh { name = "Severed " + part };
            if (outVerts.Count > 65000) m.indexFormat = IndexFormat.UInt32;
            m.SetVertices(outVerts); m.SetNormals(outNormals); m.SetTangents(outTan); m.SetUVs(0, outUv);
            m.subMeshCount = 2;
            m.SetTriangles(skin, 0, true);
            m.SetTriangles(flesh, 1, true);

            var go = new GameObject("Severed " + part);
            go.transform.SetPositionAndRotation(centroid, frame);
            go.AddComponent<MeshFilter>().sharedMesh = m;
            var r2 = go.AddComponent<MeshRenderer>();
            r2.sharedMaterials = new[] { skinMaterial, FleshMaterial() };
            go.AddComponent<FleshSurface>();
            go.AddComponent<OwnedMesh>().mesh = m;

            Vector3 size = max - min;
            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 2;
            cap.center = (min + max) * 0.5f;
            cap.radius = Mathf.Max(0.02f, Mathf.Max(size.x, size.y) * 0.32f);
            cap.height = Mathf.Max(size.z, cap.radius * 2f);

            var rb = go.AddComponent<Rigidbody>();
            rb.mass = part == HumanBodyBones.Head ? 4f : part == HumanBodyBones.LeftUpperLeg || part == HumanBodyBones.RightUpperLeg ? 12f : 4f;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            rb.linearDamping = 0.05f;
            rb.angularDamping = 0.5f;
            Vector3 inherited = rootBody && !rootBody.isKinematic ? rootBody.linearVelocity : Vector3.zero;
            rb.linearVelocity = inherited + throwDirection.normalized * throwSpeed + Vector3.up * 1.2f + Random.insideUnitSphere * 0.8f;
            rb.angularVelocity = Random.onUnitSphere * Random.Range(4f, 12f);

            Destroy(go, pieceLifetime);
            LoosePieces.Enqueue(go);
            while (LoosePieces.Count > MaxLoosePieces)
            {
                var oldest = LoosePieces.Dequeue();
                if (oldest) Destroy(oldest);
            }
        }

        static Material FleshMaterial()
        {
            if (fleshMaterial) return fleshMaterial;
            var baseMat = GraphicsSettings.currentRenderPipeline ? GraphicsSettings.currentRenderPipeline.defaultMaterial : null;
            fleshMaterial = baseMat ? new Material(baseMat) : new Material(Shader.Find("Standard"));
            fleshMaterial.name = "Wound Flesh";
            Color c = new Color(0.28f, 0.03f, 0.03f);
            fleshMaterial.color = c;
            if (fleshMaterial.HasProperty("_BaseColor")) fleshMaterial.SetColor("_BaseColor", c);
            if (fleshMaterial.HasProperty("_Smoothness")) fleshMaterial.SetFloat("_Smoothness", 0.7f);
            return fleshMaterial;
        }

        /// <summary>Frees a generated mesh together with its object.</summary>
        class OwnedMesh : MonoBehaviour
        {
            public Mesh mesh;
            void OnDestroy() { if (mesh) Destroy(mesh); }
        }
    }
}
