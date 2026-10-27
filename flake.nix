{
  description = "FlugelKranz — Monado / WiVRn 自由飛行・無限歩行ツール";

  inputs.nixpkgs.url = "github:NixOS/nixpkgs/nixos-unstable";

  outputs = { nixpkgs, ... }:
    let
      systems = [
        "x86_64-linux"
        "aarch64-linux"
      ];

      forEachSystem = nixpkgs.lib.genAttrs systems;

      runtimeDeps = system: with nixpkgs.legacyPackages.${system}; [
        fontconfig
        libGL
        libX11
        libXcursor
        libXext
        libXi
        libXrandr
        libxkbcommon
        openxr-loader
        wayland
      ];
    in
    {
      # silver: packages.default is enough for nix build and nix run.
      packages = forEachSystem (system:
        with nixpkgs.legacyPackages.${system};
        let
          package = buildDotnetModule {
            pname = "flugelkranz";
            version = "1.0.0";

            src = ./.;

            projectFile = "src/FlugelKranz/FlugelKranz.csproj";
            nugetDeps = ./nuget-deps.json;
            dotnet-sdk = dotnetCorePackages.sdk_10_0;
            dotnet-runtime = dotnetCorePackages.runtime_10_0;
            executables = [ "FlugelKranz" ];

            runtimeDeps = runtimeDeps system;

            meta = {
              description = "Monado / WiVRn 向けの自由飛行・無限歩行ツール";
              homepage = "https://github.com/ReinaS-64892/FlugelKranz";
              license = lib.licenses.gpl3Only;
              mainProgram = "FlugelKranz";
              platforms = lib.platforms.linux;
            };
          };
        in
        {
          default = package;
          flugelkranz = package;
        });

      devShells = forEachSystem (system:
        with nixpkgs.legacyPackages.${system};
        {
          default = mkShell {
            packages = [ dotnetCorePackages.sdk_10_0 nuget-to-json ] ++ runtimeDeps system;

            shellHook = ''
              export DOTNET_CLI_TELEMETRY_OPTOUT=1
              export DOTNET_NOLOGO=1
              export LD_LIBRARY_PATH="${lib.makeLibraryPath (runtimeDeps system)}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
            '';
          };
        });

    };
}
