using System.IO.Compression;
using OpenSynapse.Core.Lighting;
using Xunit;

namespace OpenSynapse.Core.Tests;

public sealed class StudioSynapse3ImportTests
{
    [Fact]
    public void ReadsDeviceLayoutAndEffectLayers()
    {
        var profiles = StudioSynapse3Import.ParseProfiles(ProfileXml);
        var profile = Assert.Single(profiles);
        Assert.Equal("Gaming", profile.Title);

        var placement = Assert.Single(profile.Devices);
        Assert.Equal(0x02B7, placement.ProductId);
        Assert.Equal("1", placement.InstanceId);
        Assert.Equal(400, placement.X);
        Assert.Equal(200, placement.Y);

        Assert.Equal(2, profile.Items.Count);
        var wave = profile.Items[0];
        Assert.False(wave.IsGroup);
        Assert.Equal("WAVE", wave.Title);
        Assert.Equal(StudioEffectKind.Wave, wave.Params.Effect);
        Assert.Equal(20, wave.Params.Width);
        Assert.Equal(10, wave.Params.Speed);
        Assert.Equal(25, wave.Params.Split);
        Assert.Equal(2000, wave.Params.PauseMs);
        Assert.Equal(90, wave.Params.Angle);
        Assert.Equal(StudioPlaybackStart.OnPress, wave.Params.PlaybackStart);
        Assert.Equal(StudioPlaybackEnd.After, wave.Params.PlaybackEnd);
        Assert.Equal(3, wave.Params.Cycles);

        var waveRegion = Assert.Single(wave.Regions);
        Assert.Equal(new StudioRgb(255, 0, 0), waveRegion.Params.Primary);
        Assert.Equal(new StudioRgb(0, 0, 255), waveRegion.Params.Secondary);
        Assert.Equal(2, waveRegion.Params.ColorStops.Count);
        var waveDevice = Assert.Single(waveRegion.Devices);
        Assert.Equal("1", waveDevice.InstanceId);
        var waveLed = Assert.Single(waveDevice.Leds);
        Assert.Equal(1, waveLed.Row);
        Assert.Equal(2, waveLed.Col);

        var group = profile.Items[1];
        Assert.True(group.IsGroup);
        Assert.Equal("Group A", group.Title);
        var staticLayer = Assert.Single(group.Children);
        Assert.Equal(StudioEffectKind.Solid, staticLayer.Params.Effect);
        Assert.Equal(new StudioRgb(255, 0, 0), staticLayer.Params.Primary);
        Assert.Single(staticLayer.Regions);
    }

    [Fact]
    public void ImportsEveryXmlProfileInAZipAndRemapsTheConnectedDevice()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-syn3-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddEntry(archive, "one.xml", ProfileXml.Replace("<Name>Gaming</Name>", "<Name>One</Name>"));
                AddEntry(archive, "two.xml", ProfileXml.Replace("<Name>Gaming</Name>", "<Name>Two</Name>"));
                AddEntry(archive, "thumbnail.bin", "not xml");
            }

            var result = StudioSynapse3Import.Import(
                path,
                [new StudioDeviceMatch(0x02B7, @"\\?\hid#vid_1532&pid_02b7")]);

            Assert.Equal(2, result.Project.Profiles.Count);
            Assert.Equal("One", result.Project.Profiles[0].Title);
            Assert.Equal("Two", result.Project.Profiles[1].Title);
            Assert.Equal(
                @"\\?\hid#vid_1532&pid_02b7",
                Assert.Single(result.Project.Profiles[0].Devices).InstanceId);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ChromaFileServiceDispatchesZipToTheSynapse3Reader()
    {
        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-syn3-{Guid.NewGuid():N}.zip");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddEntry(archive, "profile.xml", ProfileXml);
            }

            var result = StudioChromaFileService.Import(
                path,
                [new StudioDeviceMatch(0x02B7, "hid-path")]);

            Assert.Single(result.Project.Profiles);
            Assert.Equal(StudioEffectKind.Wave, result.Project.Profiles[0].Items[0].Params.Effect);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void RejectsFilesWithoutLightingEffects()
    {
        Assert.Empty(StudioSynapse3Import.ParseProfiles("<root />"));

        var path = Path.Combine(Path.GetTempPath(), $"opensynapse-syn3-{Guid.NewGuid():N}.xml");
        try
        {
            File.WriteAllText(path, "<root />");
            Assert.Throws<InvalidOperationException>(() => StudioSynapse3Import.Import(path, []));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MapsSynapseAudioAndWheelNames()
    {
        var profile = Assert.Single(StudioSynapse3Import.ParseProfiles(LayerXml("audiometer", "colorwheel")));

        Assert.Equal(2, profile.Items.Count);
        // Synapse writes the bottom of the stack first; the studio keeps the top first.
        Assert.Equal("COLORWHEEL", profile.Items[0].Title);
        Assert.Equal(StudioEffectKind.Wheel, profile.Items[0].Params.Effect);
        Assert.Equal("AUDIOMETER", profile.Items[1].Title);
        Assert.Equal(StudioEffectKind.Audio, profile.Items[1].Params.Effect);
    }

    private static void AddEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream);
        writer.Write(content);
    }

    private static string LayerXml(params string[] effects)
    {
        var layers = string.Join(
            Environment.NewLine,
            effects.Select(effect => $"<EffectLayer><Effect>{effect}</Effect><Visible>true</Visible></EffectLayer>"));
        return $"<LightingEffects><Name>Layers</Name><EffectLayers>{layers}</EffectLayers></LightingEffects>";
    }

    private const string ProfileXml = """
        <LightingEffects>
          <Name>Gaming</Name>
          <DeviceLayout>
            <DevPositions>
              <Item>
                <Key>
                  <Device>
                    <Product_ID>695</Product_ID>
                    <Vendor_ID>5426</Vendor_ID>
                    <Name>Razer Blade 16</Name>
                    <Layout>1</Layout>
                    <RegionId>2147483649</RegionId>
                  </Device>
                </Key>
                <Value>
                  <DevPos><X>10</X><Y>5</Y><Angle>0</Angle></DevPos>
                </Value>
              </Item>
            </DevPositions>
          </DeviceLayout>
          <EffectLayers>
            <EffectLayer>
              <Effect>none</Effect>
              <Visible>true</Visible>
              <GroupId>Group A</GroupId>
            </EffectLayer>
            <EffectLayer>
              <Effect>static</Effect>
              <Visible>true</Visible>
              <GroupId>Group A</GroupId>
              <Regions>
                <EffectRegion>
                  <Name>All</Name>
                  <Cells>
                    <Item>
                      <Key><Device><Product_ID>695</Product_ID><RegionId>2147483649</RegionId></Device></Key>
                      <Value>
                        <ArrayOfDeviceCell>
                          <DeviceCell><Row>0</Row><Col>0</Col></DeviceCell>
                        </ArrayOfDeviceCell>
                      </Value>
                    </Item>
                  </Cells>
                  <Colors>
                    <RzColor><Red>255</Red><Green>0</Green><Blue>0</Blue></RzColor>
                  </Colors>
                </EffectRegion>
              </Regions>
            </EffectLayer>
            <EffectLayer>
              <Effect>wave</Effect>
              <Visible>true</Visible>
              <Regions>
                <EffectRegion>
                  <Name>All</Name>
                  <Cells>
                    <Item>
                      <Key><Device><Product_ID>695</Product_ID><RegionId>2147483649</RegionId></Device></Key>
                      <Value>
                        <ArrayOfDeviceCell>
                          <DeviceCell><Row>1</Row><Col>2</Col></DeviceCell>
                        </ArrayOfDeviceCell>
                      </Value>
                    </Item>
                  </Cells>
                  <Colors>
                    <RzColor><Red>255</Red><Green>0</Green><Blue>0</Blue></RzColor>
                    <RzColor><Red>0</Red><Green>0</Green><Blue>255</Blue></RzColor>
                  </Colors>
                  <Stops><unsignedInt>0</unsignedInt><unsignedInt>100</unsignedInt></Stops>
                  <Width>20</Width>
                  <Speed>10</Speed>
                  <Split>25</Split>
                  <Pause>2</Pause>
                  <Angle>90</Angle>
                  <Start>onkeypress</Start>
                  <End>after 1 cycle</End>
                  <Cycle>3</Cycle>
                </EffectRegion>
              </Regions>
            </EffectLayer>
          </EffectLayers>
        </LightingEffects>
        """;
}