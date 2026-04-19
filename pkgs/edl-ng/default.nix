{ lib
, buildDotnetModule
, dotnetCorePackages
, libusb1
,
}:
buildDotnetModule {
  pname = "edl-ng";
  version = "1.5.0";

  src = ../..;

  runtimeDeps = [
    libusb1
  ];

  projectFile = "QCEDL.CLI/QCEDL.CLI.csproj";

  dotnetInstallFlags = [ "-p:PublishAot=false" ];

  # File generated with `nix build .#edl-ng.passthru.fetch-deps`.
  nugetDeps = ./deps.json;

  executables = [ "edl-ng" ];

  postInstall = ''
    install -Dm0644 ${../../resources/udev/99-edl-ng.rules} \
      $out/lib/udev/rules.d/99-edl-ng.rules
  '';

  dotnet-sdk = dotnetCorePackages.sdk_9_0;

  meta = {
    description = "A modern, user-friendly tool for interacting with Qualcomm devices in Emergency Download (EDL) mode";
    homepage = "https://github.com/strongtz/edl-ng";
    license = lib.licenses.mit;
    mainProgram = "edl-ng";
    platforms = lib.platforms.all;
    sourceProvenance = with lib.sourceTypes; [
      fromSource
      binaryNativeCode # libraries fetched by NuGet
    ];
  };
}
