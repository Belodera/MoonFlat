using MoonSharp.Interpreter;

var checks = new (string Name, string Source)[]
{
    ("lua-basics", "return 6 * 7 == 42"),
    ("direct-table-iteration", "for k,v in {apple=9} do return k=='apple' and v==9 end return false"),
    ("sparse-table-and-array-prefix", "local t={[1]=10,[2]=20,[4]=40,name=7}; local n=0; for k,v in t do n=n+1 end; return n==4 and #t==2"),
    ("delete-current-key-during-iteration", "local t={a=1,b=2,c=3};local n=0;for k,v in pairs(t)do t[k]=nil;n=n+1 end;return n==3 and next(t)==nil"),
    ("custom-iterator", "local t=setmetatable({},{__iter=function()return next,{a=9},nil end});local n=0;for k,v in t do n=n+v end;return n==9"),
    ("pairs-and-ipairs", "local n=0;for k,v in pairs({a=9})do n=n+v end;for k,v in ipairs({3,4})do n=n+k*v end;return n==20"),
    ("coroutine-yield-and-resume", "local co=coroutine.create(function()coroutine.yield(7);return 9 end);local ok,a=coroutine.resume(co);if not(ok and a==7)then return false end;local ok2,b=coroutine.resume(co);return ok2 and b==9"),
    ("tail-recursion", "local function f(n,a)if n==0 then return a end;return f(n-1,a+1)end;return f(10000,0)==10000"),
    ("arithmetic-metamethods", "local t=setmetatable({},{__add=function(a,b)return 11 end,__unm=function()return 12 end});return 1+t==11 and -t==12"),
    ("frozen-table", "local t={x=1};table.freeze(t);return table.isfrozen(t) and not pcall(function()t.x=2 end) and t.x==1"),
};

foreach (var (name, source) in checks)
{
    var script = new Script(CoreModules.Preset_SoftSandbox);
    var result = script.DoString(source, null, name);
    if (result.Type != DataType.Boolean || !result.Boolean)
        throw new InvalidOperationException($"Regression failed: {name}; result={result}");
    Console.WriteLine($"PASS {name}");
}

var owner = new Script(CoreModules.Preset_SoftSandbox);
var vector = Table.CreatePristineVector3(owner, new Table(owner), 1.25, -2, 3.5);
if (!vector.TryReadPristineVector3(out var x, out var y, out var z) || x != 1.25 || y != -2 || z != 3.5)
    throw new InvalidOperationException("Pristine vector fast read failed.");
if (vector.RawGet("__x")?.Number != 1.25 || vector.RawGet("__y")?.Number != -2 || vector.RawGet("__z")?.Number != 3.5)
    throw new InvalidOperationException("Pristine vector materialization failed.");
if (vector.TryReadPristineVector3(out _, out _, out _))
    throw new InvalidOperationException("Materialized vector remained in its pristine representation.");
Console.WriteLine("PASS host-neutral-pristine-vector");

var defaults = new Script(CoreModules.Preset_SoftSandbox).Options;
if (defaults.ResearchNumericFusion || defaults.ResearchRegisterExpressions || defaults.ResearchNumericLoops ||
    defaults.ResearchAffineKernel || defaults.ResearchSharedLoopMemo || defaults.ResearchLoopFixedpoint ||
    defaults.ResearchExpressionMemo || defaults.ResearchConstructorHints)
    throw new InvalidOperationException("Experimental VM options must stay disabled by default.");
Console.WriteLine("PASS experimental-optimizers-default-off");
Console.WriteLine($"MOONFLAT_REGRESSION_CHECKS passed={checks.Length + 2}");

