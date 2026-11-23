using System.Numerics;
using BinaryVibrance.Vysara;
using BinaryVibrance.Vysara.Drawing;

namespace BinaryVibrance.Vysara.Tests;

/// <summary>
/// Coverage for the neutral draw-list contract. <see cref="DrawListData.Dump"/> is the "golden draw-list" — the
/// analog of asserting a BarrierPlan/AliasPlan: a deterministic, CPU-only text snapshot of the whole
/// vertex/command stream, so the compositor pipeline up to GPU upload is tested with no backend. These tests pin
/// its format + determinism and the premultiplied-colour contract.
/// </summary>
public sealed class DrawListDataTests
{
	[Test]
	public void Empty_draw_list_dumps_a_stable_deterministic_snapshot()
	{
		const string nl = "\n";
		var expected =
			"DrawListData (rev 0, texrev 0, coords 0x0, hdr=False)" + nl +
			"  Textures (0):" + nl +
			"  Vertices (0):" + nl +
			"  Indices (0):" + nl +
			"  Commands (0):" + nl;

		DrawListData.Empty.Dump().ReplaceLineEndings("\n").ShouldBe(expected);
		// Deterministic: the same instance dumps identically every call (feeds the no-op/Revision diffing).
		DrawListData.Empty.Dump().ShouldBe(DrawListData.Empty.Dump());
	}

	[Test]
	public void A_built_draw_list_dumps_the_expected_golden_list()
	{
		var draw = BuildQuad(revision: 7);
		var dump = draw.Dump().ReplaceLineEndings("\n");

		dump.ShouldContain("DrawListData (rev 7, texrev 2, coords 1920x1080, hdr=False)");
		// The slot table: each slot's source, its size, its version and how its texels read.
		dump.ShouldContain("Textures (1):\n    [0] White 1x1 v0 premultiplied linear\n");
		dump.ShouldContain("Vertices (4):");
		// State-run batching collapses the quad to one index run; the index stream dumps in submission order.
		dump.ShouldContain("Indices (6): 0 1 2 0 2 3");
		dump.ShouldContain("Commands (1):");
		// tag=0 is the Fill primitive kind the compositor encodes; tex=0 is the white texel.
		dump.ShouldContain("[0] tag=0(Fill) tex=0 clip=(0,0 10x20) idx=0+6 vtx=0");
		// The first vertex carries its premultiplied colour + position (red, opaque, top-left).
		dump.ShouldContain("[0] pos=(0,0) uv=(0,0) col=(1,0,0,1)");
	}

	[Test]
	public void Dump_is_deterministic_across_identical_builds() =>
		BuildQuad(revision: 3).Dump().ShouldBe(BuildQuad(revision: 3).Dump());

	[Test]
	public void PremultipliedHdrColor_FromStraight_premultiplies_rgb_by_alpha()
	{
		// Straight (1, 0.5, 0.25) at alpha 0.5 → premultiplied (0.5, 0.25, 0.125, 0.5). All exactly representable
		// in Half (powers of two), so the round-trip is exact — the premultiplied-RGBA16F authoring contract.
		var c = PremultipliedHdrColor.FromStraight(1f, 0.5f, 0.25f, 0.5f);
		((float)c.R).ShouldBe(0.5f);
		((float)c.G).ShouldBe(0.25f);
		((float)c.B).ShouldBe(0.125f);
		((float)c.A).ShouldBe(0.5f);
	}

	[Test]
	public void Rect_intersect_clips_to_the_overlap_and_reports_disjoint_as_empty()
	{
		new Rect(0, 0, 100, 100).Intersect(new Rect(50, 40, 100, 100)).ShouldBe(new Rect(50, 40, 50, 60));
		new Rect(0, 0, 10, 10).Intersect(new Rect(20, 20, 5, 5)).IsEmpty.ShouldBeTrue();
	}

	// A unit quad as one Fill draw: 4 verts, 6 indices, 1 command — the minimal non-empty draw list.
	private static DrawListData BuildQuad(uint revision)
	{
		var red = PremultipliedHdrColor.FromStraight(1f, 0f, 0f, 1f);
		var verts = new OverlayVertex[]
		{
			new(new Vector2(0, 0), new Vector2(0, 0), red, Vector4.Zero),
			new(new Vector2(10, 0), new Vector2(1, 0), red, Vector4.Zero),
			new(new Vector2(10, 20), new Vector2(1, 1), red, Vector4.Zero),
			new(new Vector2(0, 20), new Vector2(0, 1), red, Vector4.Zero),
		};
		var indices = new ushort[] { 0, 1, 2, 0, 2, 3 };
		var commands = new DrawCommand[] { new(new Rect(0, 0, 10, 20), 0, 0, 6, 0, (uint)UIPrimKind.Fill) };
		return new DrawListData
		{
			Vertices = verts,
			Indices = indices,
			Commands = commands,
			CoordinateSize = new Vector2(1920, 1080),
			Revision = revision,
			TextureSlots = [TextureSource.White],
			TextureRevision = 2,
		};
	}
}
