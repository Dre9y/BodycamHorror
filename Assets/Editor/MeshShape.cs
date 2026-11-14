using System.Collections.Generic;
using UnityEngine;

namespace BodycamHorror.EditorTools
{
    /// <summary>Collects boxes, cylinders and rings into one mesh: the generated props (pipes, valves, doors)
    /// are put together from these. Texture coordinates are in metres, so scanned materials tile at their real size.</summary>
    internal sealed class MeshShape
    {
        readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        readonly List<Vector2> uvs = new List<Vector2>();
        readonly List<int> triangles = new List<int>();

        static void Basis(Vector3 axis, out Vector3 u, out Vector3 w)
        {
            u = Vector3.Cross(axis, Mathf.Abs(axis.y) < 0.9f ? Vector3.up : Vector3.right).normalized;
            w = Vector3.Cross(axis, u);
        }

        int Add(Vector3 p, Vector3 n, Vector2 uv)
        {
            vertices.Add(p); normals.Add(n); uvs.Add(uv);
            return vertices.Count - 1;
        }

        /// <summary>A box between two corners, flat-shaded.</summary>
        public void Box(Vector3 min, Vector3 max)
        {
            Vector3 centre = (min + max) * 0.5f, e = (max - min) * 0.5f;
            Vector3[] axes = { Vector3.right, Vector3.up, Vector3.forward };
            for (int axis = 0; axis < 3; axis++)
            {
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    Vector3 n = axes[axis] * sign;
                    Vector3 a = axes[(axis + 1) % 3], b = axes[(axis + 2) % 3];
                    if (sign < 0) { Vector3 swap = a; a = b; b = swap; }   // keep a x b = n
                    Vector3 face = centre + Vector3.Scale(n, e), ea = Vector3.Scale(a, e), eb = Vector3.Scale(b, e);
                    int first = vertices.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        Vector3 corner = face + ea * (i == 0 || i == 3 ? -1f : 1f) + eb * (i < 2 ? -1f : 1f);
                        Add(corner, n, new Vector2(Vector3.Dot(corner, a), Vector3.Dot(corner, b)));
                    }
                    triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
                    triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
                }
            }
        }

        /// <summary>A flat four-cornered face, wound whichever way makes it face <paramref name="facing"/>.</summary>
        public void Quad(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, Vector3 facing)
        {
            Vector3 n = Vector3.Cross(p1 - p0, p2 - p0).normalized;
            if (Vector3.Dot(n, facing) < 0f)
            {
                Vector3 swap = p1; p1 = p3; p3 = swap;
                n = -n;
            }
            Vector3 t = (p1 - p0).normalized, bt = Vector3.Cross(n, t);
            int first = vertices.Count;
            Add(p0, n, new Vector2(Vector3.Dot(p0, t), Vector3.Dot(p0, bt)));
            Add(p1, n, new Vector2(Vector3.Dot(p1, t), Vector3.Dot(p1, bt)));
            Add(p2, n, new Vector2(Vector3.Dot(p2, t), Vector3.Dot(p2, bt)));
            Add(p3, n, new Vector2(Vector3.Dot(p3, t), Vector3.Dot(p3, bt)));
            triangles.Add(first); triangles.Add(first + 1); triangles.Add(first + 2);
            triangles.Add(first); triangles.Add(first + 2); triangles.Add(first + 3);
        }

        /// <summary>A round stud: wide at <paramref name="a"/>, narrower at <paramref name="b"/>, its side sloped
        /// (and shaded as a slope), capped at b only.</summary>
        public void Stud(Vector3 a, Vector3 b, float baseRadius, float topRadius, int segments)
        {
            Vector3 axis = (b - a).normalized;
            Basis(axis, out Vector3 u, out Vector3 w);
            float height = Vector3.Distance(a, b), slope = baseRadius - topRadius;
            int side = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, ang = t * Mathf.PI * 2f;
                Vector3 r = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Vector3 n = (r * height + axis * slope).normalized;
                Add(a + r * baseRadius, n, new Vector2(t * baseRadius * 6.28f, 0f));
                Add(b + r * topRadius, n, new Vector2(t * baseRadius * 6.28f, height));
            }
            for (int i = 0; i < segments; i++)
            {
                int a0 = side + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                triangles.Add(a1); triangles.Add(b1); triangles.Add(b0);
            }
            int c = Add(b, axis, new Vector2(0.5f, 0.5f));
            int ring = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float ang = i / (float)segments * Mathf.PI * 2f;
                Vector3 r = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Add(b + r * topRadius, axis, new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
            }
            for (int i = 0; i < segments; i++) { triangles.Add(c); triangles.Add(ring + i); triangles.Add(ring + i + 1); }
        }

        public void Cylinder(Vector3 a, Vector3 b, float radius, int segments, bool caps = true)
        {
            Vector3 axis = (b - a).normalized;
            Basis(axis, out Vector3 u, out Vector3 w);
            float length = Vector3.Distance(a, b);

            int side = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments, ang = t * Mathf.PI * 2f;
                Vector3 n = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                Add(a + n * radius, n, new Vector2(t * radius * 6.28f, 0f));
                Add(b + n * radius, n, new Vector2(t * radius * 6.28f, length));
            }
            for (int i = 0; i < segments; i++)
            {
                int a0 = side + i * 2, b0 = a0 + 1, a1 = a0 + 2, b1 = a0 + 3;
                triangles.Add(a0); triangles.Add(a1); triangles.Add(b0);
                triangles.Add(a1); triangles.Add(b1); triangles.Add(b0);
            }
            if (!caps) return;

            for (int end = 0; end < 2; end++)
            {
                Vector3 centre = end == 0 ? a : b, n = end == 0 ? -axis : axis;
                int c = Add(centre, n, new Vector2(0.5f, 0.5f));
                int ring = vertices.Count;
                for (int i = 0; i <= segments; i++)
                {
                    float ang = i / (float)segments * Mathf.PI * 2f;
                    Vector3 r = u * Mathf.Cos(ang) + w * Mathf.Sin(ang);
                    Add(centre + r * radius, n, new Vector2(0.5f + Mathf.Cos(ang) * 0.5f, 0.5f + Mathf.Sin(ang) * 0.5f));
                }
                for (int i = 0; i < segments; i++)
                {
                    triangles.Add(c);
                    triangles.Add(end == 0 ? ring + i + 1 : ring + i);
                    triangles.Add(end == 0 ? ring + i : ring + i + 1);
                }
            }
        }

        public void Torus(Vector3 centre, Vector3 axis, float radius, float tube, int segments, int sides)
        {
            axis.Normalize();
            Basis(axis, out Vector3 u, out Vector3 w);
            int first = vertices.Count;
            for (int i = 0; i <= segments; i++)
            {
                float phi = i / (float)segments * Mathf.PI * 2f;
                Vector3 e = u * Mathf.Cos(phi) + w * Mathf.Sin(phi);
                for (int j = 0; j <= sides; j++)
                {
                    float psi = j / (float)sides * Mathf.PI * 2f;
                    Vector3 n = e * Mathf.Cos(psi) + axis * Mathf.Sin(psi);
                    Add(centre + e * radius + n * tube, n, new Vector2(i / (float)segments * radius * 6.28f, j / (float)sides * tube * 6.28f));
                }
            }
            int row = sides + 1;
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < sides; j++)
                {
                    int p00 = first + i * row + j, p10 = p00 + row, p01 = p00 + 1, p11 = p10 + 1;
                    triangles.Add(p00); triangles.Add(p10); triangles.Add(p01);
                    triangles.Add(p10); triangles.Add(p11); triangles.Add(p01);
                }
        }

        public Mesh Build(string name)
        {
            var mesh = new Mesh { name = name };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }
}
