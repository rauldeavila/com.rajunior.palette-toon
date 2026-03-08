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

    private static void BakeSmoothNormals(Mesh mesh)
    {
        Vector3[] vertices = mesh.vertices;
        Vector3[] normals = mesh.normals;
        int count = vertices.Length;

        if (count == 0 || normals == null || normals.Length != count)
            return;

        // group normals by vertex position and average them
        Dictionary<Vector3, Vector3> positionToNormal = new Dictionary<Vector3, Vector3>(count);

        for (int i = 0; i < count; i++)
        {
            Vector3 key = vertices[i];
            if (positionToNormal.TryGetValue(key, out Vector3 accumulated))
                positionToNormal[key] = accumulated + normals[i];
            else
                positionToNormal[key] = normals[i];
        }

        // write smoothed normals into tangent channel (xyz = direction, w = 0)
        Vector4[] tangents = new Vector4[count];
        for (int i = 0; i < count; i++)
        {
            Vector3 smoothed = positionToNormal[vertices[i]].normalized;
            tangents[i] = new Vector4(smoothed.x, smoothed.y, smoothed.z, 0f);
        }

        mesh.tangents = tangents;
    }
}
