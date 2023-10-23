# Requires a one line change to Godot

add the line `register_requested_device_extension(VK_KHR_EXTERNAL_MEMORY_WIN32_EXTENSION_NAME, true);` on line `506` of `drivers/vulkan/vulkan_context.cpp`

## Buidling Godot
### First time setup
You need Visual studio (with C++ and .NET support) and a Python 3.12 environment (or at least 3.7+) (I use conda for this)

Then just `pip install scons`

### Building
you can then build the whole thing with mono support using the following:
```bash
// Build Godot
scons p=windows vsproj=yes module_mono_enabled=yes

// Create the .NET bindings
"bin\godot.windows.editor.x86_64.mono.exe" --headless --generate-mono-glue modules/mono/glue

// Build the .NET assemblies
python "modules/mono/build_scripts/build_assemblies.py" --godot-output-dir=./bin
```