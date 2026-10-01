using System;
using System.Collections.Generic;
using UnityEngine;

namespace NOPerformance;

public sealed partial class RuntimeTests
{
    private void SpatialEdgeCases()
    {
        var random = new System.Random(28711);
        var original = new List<Unit>();
        var patched = new List<Unit>();
        var unit = Component<Unit>();
        int cases = 0;
        foreach (var size in new[] { (3500f, 1000f), (10001.25f, 777.125f), (32768f, 513.5f) })
        {
            BattlefieldGrid.GenerateGrid(size.Item1, size.Item2);
            for (int i = 0; i < BattlefieldGrid.gridLookup.Length; i++)
            {
                var list = BattlefieldGrid.gridLookup[i].units;
                list.Add(unit);
                if (i % 3 == 0) list.Add(unit);
                if (i % 11 == 0) list.Add(null!);
            }
            for (int i = 0; i < 400; i++)
            {
                var p = new GlobalPosition((float)(random.NextDouble() - .5) * size.Item1 * 3, 0,
                    (float)(random.NextDouble() - .5) * size.Item1 * 3);
                float range = i % 3 == 0 ? size.Item2 : (float)random.NextDouble() * size.Item1;
                OriginalUnits(p, range, original);
                BattlefieldGrid.GetUnitsInRangeNonAlloc(p, range, patched);
                Same(original, patched, "fractional grid candidates"); cases++;
            }
            foreach (float x in new[] { -size.Item1 * .5f, 0f, size.Item2, size.Item1 * .5f })
            foreach (float z in new[] { -size.Item1 * .5f, 0f, size.Item2, size.Item1 * .5f })
            foreach (float range in new[] { float.NaN, float.NegativeInfinity, float.PositiveInfinity, -float.MaxValue, float.MaxValue, -0f })
            {
                var p = new GlobalPosition(x, 0, z);
                Exception? oldError = null, newError = null;
                try { OriginalUnits(p, range, original); } catch (Exception ex) { oldError = ex; }
                try { BattlefieldGrid.GetUnitsInRangeNonAlloc(p, range, patched); } catch (Exception ex) { newError = ex; }
                Check(oldError?.GetType() == newError?.GetType(), "nonfinite range exception parity");
                Same(original, patched, "nonfinite range partial candidates"); cases++;
            }
        }
        BattlefieldGrid.GenerateGrid(1000, 1000);
        BattlefieldGrid.gridLookup[0] = null!; // Both methods must retain the game's failure.
        Exception? originalFault = null, patchedFault = null;
        try { OriginalUnits(default, 1000, original); } catch (Exception ex) { originalFault = ex; }
        try { BattlefieldGrid.GetUnitsInRangeNonAlloc(default, 1000, patched); } catch (Exception ex) { patchedFault = ex; }
        Check(originalFault is NullReferenceException && patchedFault?.GetType() == originalFault.GetType(), "null grid cell exception parity");
        Same(original, patched, "null grid partial output");
        originalFault = patchedFault = null;
        try { OriginalUnits(default, 1000, null!); } catch (Exception ex) { originalFault = ex; }
        try { BattlefieldGrid.GetUnitsInRangeNonAlloc(default, 1000, null!); } catch (Exception ex) { patchedFault = ex; }
        Check(originalFault is NullReferenceException && patchedFault?.GetType() == originalFault.GetType(), "null output exception parity");
        BattlefieldGrid.Clear();
        Write("Spatial extra cases=" + cases + " fractional grid/range queries plus null-cell/null-output exception parity.");
    }
}
