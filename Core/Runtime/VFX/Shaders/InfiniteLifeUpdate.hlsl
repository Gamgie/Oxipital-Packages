/*
Infinite life mode (OrbGroup.infiniteLife), Update part. See InfiniteLifeInit.hlsl for how ranks are given.

For particles spawned in infinite life mode (infiniteRank > 0) :
- rank above the expected count (OrbGroup.infiniteCount) -> killed, so lowering the intensity removes particles.
- otherwise the age is held just after the fade-in (INFINITE_LIFE_HOLD) so they never die and over-life curves
  (size, Color Over Life) stay visible.
- when infinite life is switched off (or the orb is killed), the rank is cleared : the particle ages again and dies normally.

Placed at the end of Update, buffer is the "Orb Buffer".

Known limitations :
- in UnlitAdditive render, dark particles are still killed at spawn (KillDarkParticles), so a few less than infiniteCount may be alive.
- if the effect is not simulated during a frame where the count changes (culled, paused), counts can drift.
  Switching infiniteLife off and on resyncs them.
*/

#include "OxipitalHelpers.hlsl"

/*
Bounds box (OrbGroup.boundsMode), every particle, before the infinite life part: keeps the particles in a box aligned
on the world axes, centred on the group. The velocity is integrated after this block, so a particle out of the box
is put back before it moves.
- Wall: back on the face, its outward speed bounced (strength = bounce, 0 = slides along the wall).
- Soft: pulled back by a spring that stiffens the further out it is, braked while out (strength = pull). A force
  pushing outwards holds particles outside, but never further than a quarter of the box size beyond a face.
- Wrap: comes back in through the opposite face (the box tiles space: density stays even under turbulence).
Written inline (not a helper function): the Custom HLSL block finds the attributes it reads and writes in this body.
*/

void UpdateInfiniteLife(inout VFXAttributes attributes, in StructuredBuffer<Single> buffer)
{
    int boundsMode = (int)round(getBufferFloatProperty(ORB_BOUNDS_MODE, buffer));
    if (boundsMode > 0)
    {
        float3 halfSize = max(getBufferVectorProperty(ORB_BOUNDS_SIZE, buffer), 0.01) * 0.5;
        float3 center = getBufferVectorProperty(ORB_BOUNDS_CENTER, buffer);
        float3 p = attributes.position - center;
        float3 outside = step(halfSize, abs(p));                    // 1 on each axis where it is out
        if (any(outside > 0))
        {
            float strength = getBufferFloatProperty(ORB_BOUNDS_STRENGTH, buffer);
            float3 s = (p >= 0) * 2.0 - 1.0;                         // outward direction per axis
            float3 vn = attributes.velocity * s;                     // outward speed per axis
            if (boundsMode == 1)
            {
                p = clamp(p, -halfSize, halfSize);
                vn = lerp(vn, -vn * strength, outside * (vn > 0));
            }
            else if (boundsMode == 2)
            {
                float dt = getBufferFloatProperty(ORB_BOUNDS_STEP, buffer);
                float3 over = max(abs(p) - halfSize, 0);
                vn -= over * (1.0 + over) * (strength * 20.0 * dt);   // spring, stiffer the further out
                vn *= 1.0 - outside * saturate(lerp(3.0, 10.0, vn > 0) * strength * dt); // braked, hard when going out
                // Never further out than a quarter of the box: a strong force cannot push them away
                float3 limit = halfSize * 1.5;
                float3 atLimit = step(limit, abs(p));
                p = clamp(p, -limit, limit);
                vn = lerp(vn, min(vn, 0), atLimit);
            }
            else
            {
                p -= s * halfSize * 2.0 * outside;
            }
            attributes.velocity = vn * s;
            attributes.position = p + center;
        }
    }

    if (attributes.infiniteRank == 0) return;

    if (getBufferFloatProperty(ORB_INFINITE_LIFE, buffer) < 0.5)
    {
        attributes.infiniteRank = 0;
        return;
    }

    if (attributes.infiniteRank > (uint)getBufferFloatProperty(ORB_INFINITE_COUNT, buffer))
    {
        attributes.alive = false;
        return;
    }

    attributes.age = min(attributes.age, attributes.lifetime * INFINITE_LIFE_HOLD);
}
