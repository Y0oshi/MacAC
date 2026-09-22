using System.Collections.Immutable;
using System.Numerics;
using MacAC.Mechanics.Kinetics;
using MacAC.Mechanics.Kinetics.Gait;
using MacAC.Sim.Actors;

namespace MacAC.Sim.Kinetics;

internal readonly record struct SimPeerKineticsCapture(
    Vector3 Position,
    Quaternion Orientation,
    uint FullCellId);
