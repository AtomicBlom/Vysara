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

   The endpoint loads a tap only from the folders RoseMCP finds taps in, and from the folders beneath them:

   - the taps folder of RoseMCP's install root, `%LOCALAPPDATA%/BinaryVibrance/RoseMCP/taps` (local application data
     as .NET finds it, so `~/.local/share/BinaryVibrance/RoseMCP/taps` on Linux), or `$ROSEMCP_DEPLOY_ROOT/taps` where
     that variable is set, which is where `deploy-tap.ps1` puts it;
   - each folder `ROSEMCP_TAPS` names, separated as a PATH is. An entry that is not a full path is left out, since the
     game and RoseMCP would read it against different folders.

   Symbolic links and junctions are followed to where they lead, so a link in one of those folders lets in nothing from
   outside them. The folders RoseMCP's settings name are not read: to load a tap from one, pass the folders to `Start`
   yourself, `DiagnosticsEndpoint.Start([.. DiagnosticsEndpoint.DefaultTapDirectories(), folder])`. Folders passed to
   `Start` take the place of the defaults; no call loads a tap from any path the game can read.

`Start` does nothing unless diagnostics are on, and they are always off under NativeAOT, which `./tools/check-aot.ps1`
checks by NativeAOT-publishing `SUIsei.RoseTap.Sample` and running it. Then `rose_debug_attach` or
`rose_debug_launch` the game. When it attaches, RoseMCP finds `SUIsei.dll` among the game's modules and loads the tap.

## Threads

The tap serves each request from a thread of its own, and hands whatever reads or changes the UI to the game with
`Diagnostics.Post(Action)`, which runs it at the start of the next `UIHost.Tick` of whichever host ticks first. That
assumes every `UIHost` in the game ticks on one thread, the UI thread. A game whose hosts tick on threads of their own
would have the tap touch one host's elements from another host's thread, so the tap does not suit it as it is.

A request waits up to 30 seconds for a tick. A game that stops ticking, paused in a debugger or on a loading screen
that does not tick its hosts, leaves the request unanswered until it ticks again; the work is kept and runs then.
