using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
public static class MapLauncherTests {
 static void Assert(bool ok,string why){if(!ok)throw new Exception(why);}
 static void Reject(Action action){bool rejected=false;try{action();}catch(IOException){rejected=true;}Assert(rejected,"Unsafe map input accepted");}
 public static void Run(string scratch){
  string skins=Path.Combine(scratch,"named-skins");Directory.CreateDirectory(Path.Combine(skins,"Gun Metal"));Directory.CreateDirectory(Path.Combine(skins,"[invalid]"));Assert(AssettoCorsa.InstalledSkin(skins)=="Gun Metal","A car with only a spaced skin must be launchable");Assert(AssettoCorsa.InstalledSkin(Path.Combine(scratch,"missing-skins"))==null,"Missing skins still rejected");
  foreach(string bad in new[]{"../map","folder/map","folder\\map","C:map","CON","track.","track\n"})Assert(!Data.ContentId(bad),"Unsafe content folder");Assert(Data.ContentId("Brooklyn Park"),"Spaces in track folders");
  string source=Path.Combine(scratch,"map-source","Brooklyn Park"),surface=Path.Combine(source,"data","surfaces.ini");Directory.CreateDirectory(Path.GetDirectoryName(surface));File.WriteAllText(surface,"abc");string hash=ContentInstaller.HashFile(surface);
  var package=new {id="Brooklyn Park",kind="track",name="Brooklyn Park",installedOnly=true,sha256=hash,bytes=0,expandedBytes=0,archiveRoot="Brooklyn Park",checks=new[]{new{path="data/surfaces.ini",sha256=hash,bytes=3}}};
  var manifest=AssetManifest.Parse(Data.Json.DeserializeObject(Data.Json.Serialize(new{schema=2,game="assetto-corsa",revision=hash,assets=new[]{package}})));
  string game=Path.Combine(scratch,"map-game");Directory.CreateDirectory(game);var vault=new AssetVault(Path.Combine(scratch,"map-library"));
  var missing=vault.Plan(game,manifest,new string[0]);Assert(missing.Choices.Length==1&&!missing.Choices[0].Download&&missing.DownloadBytes==0,"Missing owned track must not promise a download");Assert(vault.Review(missing).Contains("Steam"),"Missing map guidance");Reject(()=>vault.Apply("map-room",game,manifest,CancellationToken.None,null,missing));
  var reuse=vault.Plan(game,manifest,new[]{Path.GetDirectoryName(source)});Assert(reuse.Choices[0].Folder==source&&reuse.DownloadBytes==0,"Find installed map with spaces");vault.Apply("map-room",game,manifest,CancellationToken.None,null,reuse);Assert(vault.Missing(game,manifest).Length==0,"Reuse installed map without copying");
  var server=new ServerEntry{Id="managed-assetto-maps",Game="assetto-corsa",Track="ks_nordschleife",Layout="touristenfahrten",Models=new[]{"test_car"}};
  string carFolder=Path.Combine(scratch,"owned-cars","test_car");Directory.CreateDirectory(carFolder);File.WriteAllText(Path.Combine(carFolder,"data.acd"),"abc");
  var ownedCar=new {id="test_car",kind="car",name="Drift car",installedOnly=true,sha256=hash,bytes=0,expandedBytes=0,archiveRoot="test_car",checks=new[]{new{path="data.acd",sha256=hash,bytes=3}}};
  var carManifest=AssetManifest.Parse(Data.Json.DeserializeObject(Data.Json.Serialize(new{schema=2,game="assetto-corsa",revision=hash,assets=new[]{ownedCar}})));
  var carReuse=vault.Plan(game,carManifest,new[]{Path.GetDirectoryName(carFolder)});Assert(carReuse.Choices[0].Folder==carFolder&&carReuse.DownloadBytes==0,"Reuse owned drift car without a fabricated download");vault.Apply("drift-room",game,carManifest,CancellationToken.None,null,carReuse);Assert(vault.Missing(game,carManifest).Length==0,"Installed-only car linked");
  var grant=new Dictionary<string,object>{{"schema",2},{"game",server.Game},{"server",server.Id},{"track",server.Track},{"layout",server.Layout},{"model","test_car"},{"host","51.81.35.190"},{"gamePort",9610},{"httpPort",8082},{"password","mg1.abc."+new string('a',43)},{"expiresAt",DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()+90000},{"displayName","Test Driver"}};
  AssettoCorsa.ValidateGrant(grant,server,"test_car");string race=AssettoCorsa.RaceConfig(grant,"", "76561198000000000","blue","");Assert(race.Contains("TRACK=ks_nordschleife\r\nCONFIG_TRACK=touristenfahrten\r\n"),"Selected layout must reach Assetto");grant["layout"]="endurance";Reject(()=>AssettoCorsa.ValidateGrant(grant,server,"test_car"));
  Console.WriteLine("Map launcher checks passed: installed tracks, safe folders, layout binding, missing-content handling.");
 }
}
