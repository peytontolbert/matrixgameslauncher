using System;
using System.Collections.Generic;
using System.IO;

public static class MapSettingsTests {
    static void Check(bool condition,string label){if(!condition)throw new Exception(label);}
    static void Reject(Action action){try{action();}catch(IOException){return;}throw new Exception("Invalid map settings accepted");}
    public static void Run(){
        var supported=new Dictionary<string,object>{{"id","supported"},{"name","Supported map"},{"traffic",new Dictionary<string,object>{{"supported",true},{"defaultEnabled",false}}},{"teleportCount",5}};
        var unsupported=new Dictionary<string,object>{{"id","unsupported"},{"name","No routes"},{"traffic",new Dictionary<string,object>{{"supported",false},{"reason","No route installed"}}},{"teleportCount",0}};
        var server=new Dictionary<string,object>{{"mapId","supported"},{"selectionRevision",new string('a',64)},{"trafficEnabled",false}};
        var payload=new Dictionary<string,object>{{"server",server},{"maps",new object[]{supported,unsupported}},{"canSwitch",true}};
        var state=MapSettingsState.Parse(payload);Check(state.Maps[0].TrafficSupported&&state.Maps[0].TeleportCount==5,"Map capabilities preserved");
        var request=Data.Object(Data.Json.DeserializeObject(Data.Json.Serialize(state.Request("supported",true,true))));Check((bool)request["trafficEnabled"]&&(bool)request["confirmRestart"],"Explicit traffic and restart selection sent");
        Check(Data.Text(request,"revision")==new string('a',64),"Selection revision preserved");
        Reject(()=>state.Request("unsupported",true,true));Reject(()=>state.Request("unknown",false,true));Reject(()=>state.Request("supported",true,false));
        state.CanSwitch=false;Reject(()=>state.Request("supported",false,true));
        server["selectionRevision"]="invalid";Reject(()=>MapSettingsState.Parse(payload));
    }
}
