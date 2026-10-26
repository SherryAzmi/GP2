# Meta XR SDK setup (Quest)

Unity 6000.3 / URP. Open the project in Unity Hub, then:

1. **Tools > Meta XR > 1. Install Meta XR SDK** – installs `com.meta.xr.sdk.all` and
   XR Plug-in Management through Package Manager (latest compatible versions).
2. **Tools > Meta XR > 2. Configure Project for Quest** – sets IL2CPP, ARM64, min API 29,
   Vulkan/GLES3, Linear color space, switches build target to Android, and replaces the
   template package name with `com.gp2.vrproject` (change it in Player Settings if you want).
3. **Edit > Project Settings > XR Plug-in Management** > Android tab > enable **Oculus**
   (or **OpenXR** with the *Meta Quest Support* feature group).
4. **Edit > Project Settings > Meta XR** (Project Setup Tool) > **Fix All** to apply any remaining
   recommended settings.
5. In your scene add the rig: **Meta XR > Building Blocks > Camera Rig** (plus Passthrough,
   Hand/Controller Tracking, Grab Interaction etc. as needed). Delete the default Main Camera.
6. Quest: enable Developer Mode, connect via USB, **File > Build Profiles > Android > Build And Run**.

Notes
- This project uses the new Input System (`activeInputHandler: 1`), which Meta XR supports.
- For Quest, use the `Mobile_RPAsset` / `Mobile_Renderer` URP assets for the Android quality level.
