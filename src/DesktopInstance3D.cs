using Godot;
using System;
using Desktop;

public partial class DesktopInstance3D : MeshInstance3D
{
	DesktopDuplicator Desktop;

	bool captureSetup = false;

	double acc = 0;

	// Called when the node enters the scene tree for the first time.
	public override void _Ready()
	{
		Desktop = new DesktopDuplicator();

		var material = new StandardMaterial3D();
		material.AlbedoTexture = Desktop.gdTexture;
		MaterialOverride = material;

		Desktop.CaptureDesktop();
	}

	// Called every frame. 'delta' is the elapsed time since the previous frame.
	public override void _Process(double delta)
	{
		acc += delta;
		if (acc > 1) // 1FPS for testing
		{
            acc = 0;
            Desktop.CaptureDesktop();
		}
	}
}
