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

    // snap position to a grid so float-precision differences don't prevent grouping
    private static long HashPosition(Vector3 v)
    {
        // 0.0001 unit grid — well below visual threshold
        long x = Mathf.RoundToInt(v.x * 10000f);
        long y = Mathf.RoundToInt(v.y * 10000f);
        long z = Mathf.RoundToInt(v.z * 10000f);
        return x * 73856093L ^ y * 19349669L ^ z * 83492791L;
    }

    private static void BakeSmoothNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int count = vertices.Length;

        if (count == 0 || normals == null || normals.Length != count)
            return;

        // group normals by quantized vertex position and average them
        Dictionary<long, Vector3> positionToNormal = new Dictionary<long, Vector3>(count);
        long[] keys = new long[count];

        for (int i = 0; i < count; i++)
        {
            long key = HashPosition(vertices[i]);
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
