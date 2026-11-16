using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

public sealed class MapChoice {
    public string Id,Name,TrafficReason;public bool TrafficSupported,TrafficDefault,TrafficLocked;public int TeleportCount;
    public static MapChoice Parse(object raw){
        var row=Data.Object(raw);var traffic=row.ContainsKey("traffic")?Data.Object(row["traffic"]):new Dictionary<string,object>();
        var result=new MapChoice{Id=Data.Text(row,"id"),Name=Data.Text(row,"name"),TrafficReason=Data.Text(traffic,"reason"),TrafficSupported=traffic.ContainsKey("supported")&&traffic["supported"] is bool&&(bool)traffic["supported"],TrafficLocked=row.ContainsKey("trafficLocked")&&row["trafficLocked"] is bool&&(bool)row["trafficLocked"],TrafficDefault=traffic.ContainsKey("defaultEnabled")&&traffic["defaultEnabled"] is bool&&(bool)traffic["defaultEnabled"],TeleportCount=row.ContainsKey("teleportCount")?Convert.ToInt32(row["teleportCount"]):0};
        if(!Data.Id(result.Id)||result.Name.Length==0||result.Name.Length>200||result.TeleportCount<0||result.TeleportCount>256)throw new IOException("Invalid map settings response");return result;
    }
    public override string ToString(){return Name+(TrafficSupported?"  |  AI traffic available":"");}
}
public sealed class CarCatalogueChoice {
    public string Id,Name;public string[] Models;
    public static CarCatalogueChoice Parse(object raw){var d=Data.Object(raw);var rows=Data.Array(d["models"]).Select(Data.Object).ToArray();var ids=rows.Select(r=>Data.Text(r,"id")).ToArray();var c=new CarCatalogueChoice{Id=Data.Text(d,"id"),Name=Data.Text(d,"name"),Models=rows.Select(r=>Data.Text(r,"name")).ToArray()};if(!Data.Id(c.Id)||c.Name.Length<1||c.Name.Length>100||ids.Length<1||ids.Length>30||ids.Any(id=>!Data.Id(id))||ids.Distinct().Count()!=ids.Length)throw new IOException("Invalid car catalogue");return c;}
    public override string ToString(){return Name+" | "+Models.Length+" driver cars";}
}
public sealed class MapSettingsState {
    public CarCatalogueChoice[] Catalogues=new CarCatalogueChoice[0];public string CurrentCatalogue="";
    public MapChoice[] Maps;public string CurrentId,Revision;public bool CanSwitch,TrafficEnabled;
    public static MapSettingsState Parse(Dictionary<string,object> data){
        var server=Data.Object(data["server"]);var state=new MapSettingsState{Maps=Data.Array(data["maps"]).Select(MapChoice.Parse).ToArray(),CurrentId=Data.Text(server,"mapId"),Revision=Data.Text(server,"selectionRevision"),CanSwitch=data.ContainsKey("canSwitch")&&data["canSwitch"] is bool&&(bool)data["canSwitch"],TrafficEnabled=server.ContainsKey("trafficEnabled")&&server["trafficEnabled"] is bool&&(bool)server["trafficEnabled"]};
        if(data.ContainsKey("catalogues")){state.Catalogues=Data.Array(data["catalogues"]).Select(CarCatalogueChoice.Parse).ToArray();state.CurrentCatalogue=Data.Text(server,"catalogueId");if(state.Catalogues.Length<1||state.Catalogues.Length>32||state.Catalogues.Select(c=>c.Id).Distinct().Count()!=state.Catalogues.Length||!state.Catalogues.Any(c=>c.Id==state.CurrentCatalogue))throw new IOException("Car catalogues changed. Refresh the server.");}
        if(!Regex.IsMatch(state.Revision,@"\A[a-f0-9]{64}\z")||state.Maps.Length<1||state.Maps.Length>512||state.Maps.Select(m=>m.Id).Distinct().Count()!=state.Maps.Length||!state.Maps.Any(m=>m.Id==state.CurrentId))throw new IOException("Map settings changed. Update the launcher or refresh the server.");return state;
    }
    public object Request(string id,bool traffic,bool confirmed,string catalogueId=null){
        var map=Maps.SingleOrDefault(m=>m.Id==id);if(!CanSwitch)throw new IOException("Only the server owner can change map settings.");if(map==null||traffic&&!map.TrafficSupported||map.TrafficLocked&&traffic!=TrafficEnabled||!confirmed)throw new IOException("Choose supported traffic settings and confirm the server restart.");
        var request=new Dictionary<string,object>{{"mapId",id},{"revision",Revision},{"trafficEnabled",traffic},{"confirmRestart",true}};
        if(Catalogues.Length>0){catalogueId=catalogueId??CurrentCatalogue;if(!Catalogues.Any(c=>c.Id==catalogueId))throw new IOException("Choose an available car catalogue");request["catalogueId"]=catalogueId;}
        return request;
    }
}
public sealed class MapSettingsWindow : Form {
    readonly ComboBox catalogues=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Width=560};
    readonly Label roster=new Label{AutoSize=true,MaximumSize=new Size(560,150)};
    readonly MapSettingsState state;readonly ComboBox maps=new ComboBox{DropDownStyle=ComboBoxStyle.DropDownList,Dock=DockStyle.Top};
    readonly CheckBox traffic=new CheckBox{Text="Enable AI traffic",AutoSize=true},confirm=new CheckBox{Text="Restart this server and disconnect its current players.",AutoSize=true};
    readonly Label features=new Label{AutoSize=true,MaximumSize=new Size(560,120)};
    readonly Button apply=new Button{Text="Apply and restart",AutoSize=true};
    public string MapId {get{return ((MapChoice)maps.SelectedItem).Id;}}public bool TrafficEnabled {get{return traffic.Checked;}}
    public string CatalogueId {get{var choice=catalogues.SelectedItem as CarCatalogueChoice;return choice==null?state.CurrentCatalogue:choice.Id;}}
    public object Request {get{return state.Request(MapId,traffic.Checked,confirm.Checked,CatalogueId);}}
    public MapSettingsWindow(MapSettingsState current){
        state=current;Text="Server map, cars and traffic";Size=new Size(620,540);MinimumSize=Size;StartPosition=FormStartPosition.CenterParent;Font=new Font("Segoe UI",10);
        var panel=new FlowLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,Padding=new Padding(16),FlowDirection=FlowDirection.TopDown,WrapContents=false};Controls.Add(panel);
        panel.Controls.Add(new Label{Text="Server map",AutoSize=true});maps.Width=560;panel.Controls.Add(maps);
        if(state.Catalogues.Length>0){panel.Controls.Add(new Label{Text="Car catalogue (up to 30 driver models, plus traffic)",AutoSize=true});panel.Controls.Add(catalogues);panel.Controls.Add(roster);catalogues.Items.AddRange(state.Catalogues);catalogues.Enabled=state.CanSwitch;catalogues.SelectedIndexChanged+=(s,e)=>{var c=catalogues.SelectedItem as CarCatalogueChoice;roster.Text=c==null?"":string.Join(" · ",c.Models);confirm.Checked=false;if(maps.SelectedItem!=null)UpdateApply();};catalogues.SelectedItem=state.Catalogues.First(c=>c.Id==state.CurrentCatalogue);}
        panel.Controls.Add(traffic);panel.Controls.Add(features);panel.Controls.Add(confirm);
        var buttons=new FlowLayoutPanel{AutoSize=true};buttons.Controls.Add(apply);var cancel=new Button{Text="Cancel",DialogResult=DialogResult.Cancel,AutoSize=true};buttons.Controls.Add(cancel);panel.Controls.Add(buttons);CancelButton=cancel;
        maps.Items.AddRange(state.Maps);maps.SelectedIndexChanged+=(s,e)=>Selected();traffic.CheckedChanged+=(s,e)=>{confirm.Checked=false;UpdateApply();};confirm.CheckedChanged+=(s,e)=>UpdateApply();
        maps.SelectedItem=state.Maps.First(m=>m.Id==state.CurrentId);maps.Enabled=state.CanSwitch&&state.Maps.Length>1;confirm.Visible=apply.Visible=state.CanSwitch;
        apply.Click+=(s,e)=>{var request=Request;DialogResult=DialogResult.OK;Close();};
    }
    void Selected(){var map=maps.SelectedItem as MapChoice;if(map==null)return;traffic.Enabled=state.CanSwitch&&map.TrafficSupported&&!map.TrafficLocked;traffic.Checked=map.TrafficSupported&&(map.Id==state.CurrentId?state.TrafficEnabled:map.TrafficDefault);features.Text=(map.TrafficSupported?"Traffic uses the installed routes for this layout.":string.IsNullOrEmpty(map.TrafficReason)?"No native traffic route is installed for this layout.":map.TrafficReason)+"\n"+(map.TeleportCount>0?map.TeleportCount+" teleport destinations available in game.":"No authored teleport destinations found.")+(state.CanSwitch?"":"\nOnly the server owner can change these settings.");confirm.Checked=false;UpdateApply();}
    void UpdateApply(){apply.Enabled=state.CanSwitch&&confirm.Checked&&(MapId!=state.CurrentId||traffic.Checked!=state.TrafficEnabled||CatalogueId!=state.CurrentCatalogue);}
}
