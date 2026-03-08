using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bakes averaged (smooth) normals into the mesh tangent channel so the
/// outline pass can expand vertices uniformly, even on hard-edge geometry.
/// </summary>
[ExecuteAlways]
[DisallowMultipleComponent]
public class PaletteToonOutlineSmoother : MonoBehaviour
{
    private Mesh _meshInstance;
    private Mesh _originalSharedMesh;
    private MeshFilter _meshFilter;

    private void OnEnable()
    {
        Bake();
    }

    private void OnDisable()
    {
        Release();
    }

    private void OnDestroy()
    {
        Release();
    }

    public void Bake()
    {
        _meshFilter = GetComponent<MeshFilter>();
        if (_meshFilter == null) return;

        Mesh shared = _meshFilter.sharedMesh;
        if (shared == null) return;

        // already instanced and active on the filter
        if (_meshInstance != null && _meshFilter.sharedMesh == _meshInstance) return;

        Release();

        _originalSharedMesh = shared;
        _meshInstance = Instantiate(shared);
        _meshInstance.name = shared.name + " (SmoothedOutline)";

        BakeSmoothNormals(_meshInstance);

        _meshFilter.sharedMesh = _meshInstance;
    }

    private void Release()
    {
        if (_meshFilter != null && _originalSharedMesh != null)
        {
            _meshFilter.sharedMesh = _originalSharedMesh;
        }

        if (_meshInstance != null)
        {
            if (Application.isPlaying)
                Destroy(_meshInstance);
            else
                DestroyImmediate(_meshInstance);
        }

        _meshInstance = null;
        _originalSharedMesh = null;
    }

    // quantized position key — uses actual coordinates for equality,
    // hash only for bucket placement (no false merges from collisions)
    private struct QuantizedPos : System.IEquatable<QuantizedPos>
    {
        public int x, y, z;
        public QuantizedPos(Vector3 v)
        {
            // 0.0001 unit grid — well below visual threshold
            x = Mathf.RoundToInt(v.x * 10000f);
            y = Mathf.RoundToInt(v.y * 10000f);
            z = Mathf.RoundToInt(v.z * 10000f);
        }
        public bool Equals(QuantizedPos other) => x == other.x && y == other.y && z == other.z;
        public override bool Equals(object obj) => obj is QuantizedPos other && Equals(other);
        public override int GetHashCode() => x * 73856093 ^ y * 19349669 ^ z * 83492791;
    }

    private static void BakeSmoothNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int count = vertices.Length;

        if (count == 0 || normals == null || normals.Length != count)
            return;

        // group normals by quantized vertex position and average them
        Dictionary<QuantizedPos, Vector3> positionToNormal = new Dictionary<QuantizedPos, Vector3>(count);
        QuantizedPos[] keys = new QuantizedPos[count];

        for (int i = 0; i < count; i++)
        {
            QuantizedPos key = new QuantizedPos(vertices[i]);
            keys[i] = key;
            if (positionToNormal.TryGetValue(key, out Vector3 accumulated))
                positionToNormal[key] = accumulated + normals[i];
            else
                positionToNormal[key] = normals[i];
        }

        // write smoothed normals into tangent channel (xyz = direction, w = 0)
        Vector4[] tangents = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 smoothed = positionToNormal[keys[i]].normalized;
            tangents[i] = new Vector4(smoothed.x, smoothed.y, smoothed.z, 0f);
        }

        mesh.tangents = tangents;
    }
}
