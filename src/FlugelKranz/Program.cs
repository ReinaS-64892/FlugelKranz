using System.CommandLine;

var root = new RootCommand("FlugelKranz");

/*
Flugel ... 翼、有翼属 などを意味して
Kranz ... 冠、輪 などを意味して

モチーフは Botania の Flügel Tiara が元となる。
その Flügel Tiara ティアラは、装飾を付与しないと装飾がないのも相まり、よくにているような気もするし、その名前で使っても文脈的に衝突しないとは思うが、一応避けた。
*/

var libMonadoPathOption = new Option<string>("LibMonadoPath", "lib")
{
    Description = "Connect path to libmonado.so (libmonado_wivrn.so) (default : /usr/lib/wivrn/libmonado_wivrn.so)"
};

root.Options.Add(libMonadoPathOption);

root.SetAction(async (arg, cancellationToken) =>
{
    var libMonadoPath = arg.GetValue(libMonadoPathOption) ?? "/usr/lib/wivrn/libmonado_wivrn.so";
    if (File.Exists(libMonadoPath) is false) { throw new Exception("libmonado*.so not found!"); }

    // to Main(libMonadoPath);

});


return await root.Parse(args).InvokeAsync();
