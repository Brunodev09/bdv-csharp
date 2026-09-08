using System.Numerics;

namespace BdvEngine;

/// <summary>
/// A skeleton binding: the joint nodes a skinned mesh deforms with, and the inverse bind matrix
/// that takes each vertex from model space into that joint's local space.
///
/// <para>The joints are ordinary <see cref="SimObject"/>s in the scene graph — the same nodes an
/// <see cref="Animator"/> writes transforms into, and the same nodes the renderer already rebakes
/// world matrices for. Nothing about skinning needs a parallel hierarchy; a skeleton is just part
/// of the scene, which is what makes "animate a bone" and "parent a sword to a hand" the same
/// operation.</para>
///
/// <para>Per the glTF spec the palette entry for joint <c>i</c> is</para>
/// <code>
/// jointMatrix[i] = inverse(meshWorldMatrix) * jointWorldMatrix[i] * inverseBindMatrix[i]
/// </code>
/// <para>The leading inverse cancels the mesh node's own transform, which the vertex shader
/// re-applies via <c>u_model</c> — so a skinned mesh can still be moved, scaled and parented like
/// any other object.</para>
/// </summary>
public sealed class Skin
{
    /// <summary>Palette size cap. Also the shaders' <c>MAX_JOINTS</c> — keep them in step. 64 mat4s
    /// is comfortably inside the vertex-uniform budget of every GL 4.1 driver we target.</summary>
    public const int MaxJoints = 64;

    /// <summary>The joint nodes, in the order the mesh's JOINTS_0 indices refer to.</summary>
    public readonly SimObject[] Joints;

    /// <summary>Model-space → joint-space, one per joint, straight from the glTF accessor.</summary>
    public readonly Matrix4x4[] InverseBind;

    /// <summary>Scratch palette, rewritten each frame by <see cref="UpdatePalette"/>. Shared by
    /// every mesh using this skin, so it's computed once per frame, not once per draw.</summary>
    public readonly Matrix4x4[] JointMatrices;

    private int _lastUpdatedFrame = -1;

    public Skin(SimObject[] joints, Matrix4x4[] inverseBind)
    {
        if (joints.Length != inverseBind.Length)
            throw new ArgumentException(
                $"Skin: {joints.Length} joints but {inverseBind.Length} inverse bind matrices.");
        if (joints.Length > MaxJoints)
            throw new NotSupportedException(
                $"Skin has {joints.Length} joints; the shader palette holds {MaxJoints}. " +
                "Split the mesh or raise Skin.MaxJoints and the shaders' MAX_JOINTS together.");

        Joints = joints;
        InverseBind = inverseBind;
        JointMatrices = new Matrix4x4[joints.Length];
        for (int i = 0; i < JointMatrices.Length; i++) JointMatrices[i] = Matrix4x4.Identity;
    }

    public int JointCount => Joints.Length;

    /// <summary>Recompute the palette from the joints' current world matrices. Idempotent within a
    /// frame: several meshes sharing one skin (a character split by material) only pay once.</summary>
    public void UpdatePalette(in Matrix4x4 meshWorld, int frame)
    {
        if (_lastUpdatedFrame == frame) return;
        _lastUpdatedFrame = frame;

        // A non-invertible mesh transform means a zero scale somewhere; identity keeps the mesh
        // drawable (in the wrong place) rather than filling the palette with NaNs.
        if (!Matrix4x4.Invert(meshWorld, out var invMeshWorld)) invMeshWorld = Matrix4x4.Identity;

        for (int i = 0; i < Joints.Length; i++)
            JointMatrices[i] = InverseBind[i] * Joints[i].WorldMatrix * invMeshWorld;
    }
}
