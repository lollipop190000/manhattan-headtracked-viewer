using System;
using System.Linq;
using Manhattan.Viewer;
using UnityEngine;

// Test fixtures generate correspondence explicitly before residency. Production never compiles
// geometry during registration, visibility updates, or reloads.
internal static class OwnershipTestData
{
    public static SkylineOwnershipData Compile(CityTileRecord[] tiles,GameObject[] roots,string version="fixture")
    {
        var data=new SkylineOwnershipData {sourceVersion=version,
            tiles=tiles.Select((tile,i)=>SkylineOwnershipBinding.Describe(tile,roots[i],"fixture-content")).ToArray()};
        data.groups=tiles.Where(t=>t.layer==CityLayer.Skyline).Select(sky=>
        {
            using(var compiler=new OfflineOwnershipGroupCompiler())
            {
                for(int i=0;i<tiles.Length;i++)compiler.Register(tiles[i],roots[i]);
                compiler.Tick(1000000);if(compiler.Busy)throw new InvalidOperationException("Fixture compiler unfinished");
                return compiler.ExportGroup(sky.id);
            }
        }).ToArray();
        return data;
    }
    public static void Configure(SkylineGeometryOwnership ownership,CityTileRecord[] tiles,GameObject[] roots,string version="fixture")
        =>ownership.Configure(Compile(tiles,roots,version),version,_=>"fixture-content");
}
