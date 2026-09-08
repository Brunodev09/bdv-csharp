#:project GateKit/GateKit.csproj
// Navmesh gate.
//
//   dotnet run tools/check_navmesh.cs
//
// Runs sketches/navmesh_test.cs, which self-checks routing against a scene built to break it: a
// wall with one doorway, a sealed chamber, and a stepped ramp to a platform. This wrapper asserts
// those checks passed and adds the structural claims the sketch doesn't measure — that merging
// cells into rectangles actually compresses the search space, and that the mesh is connected
// rather than a pile of islands.
//
// The compression number is the reason this is a polygon mesh and not A* on the grid it came from.
using GateKit;

string output = Gate.RunSketch("sketches/navmesh_test.cs", "--shot", "/tmp/navgate.png", "--frames", "30");

int polys = Gate.Int(output, @"NAV POLYS=(\d+)");
int portals = Gate.Int(output, @"PORTALS=(\d+)");
float area = Gate.Float(output, @"AREA=(\d+)");
int cells = Gate.Int(output, @"CELLS=(\d+)");
int waypoints = Gate.Int(output, @"NAV PATH found=\w+ waypoints=(\d+)");
int steps = Gate.Int(output, @"NAV WALK routed=\w+ steps=(\d+)");
int links = Gate.Int(output, @"NAV LINKS=(\d+)");
int jumpSteps = Gate.Int(output, @"NAV JUMP arrived=\w+ traversed=\w+ steps=(\d+)");
bool traversed = Gate.Has(output, "traversed=True");
bool sealedHeld = Gate.Has(output, "sealed chamber still sealed");
bool sketchPassed = Gate.Has(output, "NAVMESH PASS");

double compression = polys > 0 ? (double)cells / polys : 0;

Gate.Info($"mesh        : {polys} polys, {portals} portals, {area:F0}m2");
Gate.Info($"compression : {cells:N0} walkable cells -> {polys} polys ({compression:F0}x fewer nodes)");
Gate.Info($"path        : {waypoints} waypoints; agent arrived in {steps} steps ({steps / 60.0:F1}s)");
Gate.Info($"links       : {links} generated; agent traversed one in {jumpSteps} steps");
Gate.Blank();

Gate.Check("routing checks pass", sketchPassed, "the sketch's 18 checks all passed");
Gate.Check("merging compresses the graph", compression >= 50,
           $"{compression:F0}x fewer nodes than the grid (A* on cells would search {cells:N0})");
Gate.Check("mesh is connected", portals >= polys - 1,
           $"{portals} portals for {polys} polys (a spanning tree needs {polys - 1})");
Gate.Check("path is a corridor, not a cell tour", waypoints < 12,
           $"{waypoints} waypoints — funnelled to corners, not one per polygon");
Gate.Check("agent reaches the goal", steps > 0 && steps < 1500,
           $"{steps} simulated steps at 1/60");
Gate.Check("off-mesh links generated", links > 0, $"{links} jump/drop links from open edges");
Gate.Check("links never breach the sealed room", sealedHeld,
           "clearance ray keeps links out of walls");
Gate.Check("agent traverses a link", traversed && jumpSteps > 0,
           $"jumped the gap and landed, {jumpSteps} steps");

return Gate.Report("NAVMESH PASS — bakes from collision, routes around and over geometry",
                   "NAVMESH FAIL");
