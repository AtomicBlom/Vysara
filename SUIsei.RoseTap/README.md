# SUIsei.RoseTap

SUIsei's tap for [RoseMCP](https://github.com/AtomicBlom/RoseMCP): it serves RoseMCP's live UI tools (`rose_xaml_tree`,
`rose_xaml_properties`, `rose_xaml_apply`, picking and the rest) for a running SUIsei game.

## Deploying

```
./tools/deploy-tap.ps1
```

This builds the tap and copies `SUIsei.RoseTap.dll` and its pdb to RoseMCP's taps folder:

```
%LOCALAPPDATA%/BinaryVibrance/RoseMCP/taps
```

`-Destination`, or `$env:ROSEMCP_DEPLOY_ROOT`, names another install root, as it does for RoseMCP's own deploy. The
taps folder sits beside RoseMCP's `settings.json`, and installing RoseMCP never clears it. RoseMCP also looks in the
folders its settings name, and then in the folders the `ROSEMCP_TAPS` environment variable names (separated as a PATH
is). The first tap it finds for a framework is the one that serves it.

Copy only the tap. The game's own SUIsei is handed to it, so a `SUIsei.dll` beside the tap would never be used. A game
that has loaded the tap holds the file open, so close it before deploying again.

## Turning it on in a game

The tap is loaded through a diagnostics endpoint, which lives in `SUIsei.Diagnostics.Endpoint`, not in SUIsei itself. A
game turns it on in two steps, usually in a Debug configuration only:

1. Turn diagnostics on in the runtime config, and reference the endpoint:

   ```xml
   <ItemGroup Condition="'$(Configuration)' == 'Debug'">
     <RuntimeHostConfigurationOption Include="SUIsei.Diagnostics.Enabled" Value="true" Trim="true" />
     <ProjectReference Include="path/to/SUIsei.Diagnostics.Endpoint/SUIsei.Diagnostics.Endpoint.csproj" />
   </ItemGroup>
   ```

2. Start the endpoint once, after the game's first `UIHost` is built:

   ```csharp
   #if DEBUG
   DiagnosticsEndpoint.Start();
   #endif
   ```

   `Start` takes the folders taps may be loaded from. Pass RoseMCP's taps folder to refuse a tap from anywhere else.

`Start` does nothing unless diagnostics are on, and they are always off under NativeAOT. Then `rose_debug_attach` or
`rose_debug_launch` the game. When it attaches, RoseMCP finds `SUIsei.dll` among the game's modules and loads the tap.
