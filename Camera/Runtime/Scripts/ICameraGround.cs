using UnityEngine;

namespace Oxipital
{
    /// <summary>
    /// The ground under a camera ride, given by the scene (a generated terrain, a heightmap...). Hand it to the Spline
    /// camera with <see cref="SplineMovement.SetGround"/>: the camera then rides at <see cref="SplineMovement.altitude"/>
    /// above it, rising early before a hill.
    /// </summary>
    public interface ICameraGround
    {
        /// <summary>World height (y) of the ground under this world position.</summary>
        float GetGroundHeight(Vector3 worldPosition);
    }
}
