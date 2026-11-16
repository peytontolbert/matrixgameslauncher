using System;
using System.IO;
public static class AssettoAssistTests {
 static void Check(bool value){if(!value)throw new Exception("Clutch assistance regression");}
 public static void Run(){
  Check(AssettoAssistLease.AutomaticClutch("automatic","[HEADER]\nINPUT_METHOD=KEYBOARD\n[OTHER]\nX=1")==true);
  Check(AssettoAssistLease.AutomaticClutch("automatic","[HEADER]\r\nINPUT_METHOD=X360\r\n")==true);
  Check(AssettoAssistLease.AutomaticClutch("automatic","[HEADER]\nINPUT_METHOD=WHEEL\n")==null);
  Check(AssettoAssistLease.AutomaticClutch("game","")==null);
  Check(AssettoAssistLease.AutomaticClutch("on","")==true);
  Check(AssettoAssistLease.AutomaticClutch("off","")==false);
  string original="[ASSISTS]\nABS=1\nAUTO_CLUTCH=0\nAUTO_SHIFTER=0\n[OTHER]\nAUTO_CLUTCH=9\n";
  string result=AssettoAssistLease.Override(original,true);
  Check(result==original.Replace("AUTO_CLUTCH=0","AUTO_CLUTCH=1"));
  Check(AssettoAssistLease.Override("[ASSISTS]\nABS=1",true).Contains("AUTO_CLUTCH=1"));
  string root=Path.Combine(Environment.GetEnvironmentVariable("MATRIX_LAUNCHER_TEST_TMP")??Path.GetTempPath(),"matrix-clutch-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  string path=Path.Combine(root,"assists.ini");File.WriteAllText(path,original);
  using(new AssettoAssistLease(root,"on")){Check(File.ReadAllText(path)==result);}
  Check(File.ReadAllText(path)==original);
  using(new AssettoAssistLease(root,"off")){File.WriteAllText(path,"user changed settings");}
  Check(File.ReadAllText(path)=="user changed settings");
  Console.WriteLine("Clutch assistance: control detection, scoped override and restoration passed.");
 }
}
