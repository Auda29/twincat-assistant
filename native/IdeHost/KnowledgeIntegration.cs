using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using ThreadHelper = Microsoft.VisualStudio.Shell.ThreadHelper;

namespace TwinCatAssistant.Native
{
    public sealed partial class AssistantControl
    {
        private readonly Border _knowledgePanel=new Border { Padding=new Thickness(12,10,12,12),Visibility=Visibility.Collapsed,BorderThickness=new Thickness(0,0,0,1) };
        private readonly TextBlock _knowledgeText=new TextBlock { TextWrapping=TextWrapping.Wrap,FontSize=12.5,Margin=new Thickness(0,5,0,8) };
        private readonly TextBlock _knowledgeSummary=new TextBlock { TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,4,0,0) };
        private readonly ComboBox _knowledgeSources=new ComboBox { DisplayMemberPath="DisplayLabel",MinHeight=30,MaxWidth=720,HorizontalAlignment=HorizontalAlignment.Stretch,Margin=new Thickness(0,4,0,8) };
        private readonly ProgressBar _knowledgeProgress=new ProgressBar { Height=3,IsIndeterminate=true,Visibility=Visibility.Collapsed,Margin=new Thickness(0,0,0,8) };
        private Button _knowledgeStart,_knowledgeCancel;
        private KnowledgeState _knowledgeState;
        private bool _knowledgeChecking,_knowledgeBusy,_knowledgeConnecting;
        private DateTime _knowledgeChecked;
        private CancellationTokenSource _knowledgeCancellation;
        private string _knowledgeConnectionKey;
        // OpenChamber's first catalog request can time out while the shared
        // backend still loads the full index after the IDE starts. That load
        // continues, so a later reconnect succeeds. Retry with growing gaps.
        private static readonly int[] KnowledgeRetrySeconds={0,20,30,45,60,90,120,180};
        private int _knowledgeRetries;
        private DateTime _knowledgeRetryAt;
        private string _knowledgeRetryKey;
        private bool KnowledgeConnected()
        {
            return _knowledgeState?.ready==true&&_chatAdapterReady&&_chatSelection?.IsSelected==true
                &&_knowledgeConnectionKey==_knowledgeState.revision+"\0"+_chatSelection.directory+"\0"+_chatDocumentGeneration;
        }

        private void BuildKnowledge()
        {
            _knowledgePanel.SetResourceReference(Border.BackgroundProperty,"Tc.Surface");
            _knowledgePanel.SetResourceReference(Border.BorderBrushProperty,"Tc.Border");
            _knowledgeText.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Foreground");
            _knowledgeSummary.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Muted");
            _knowledgeProgress.SetResourceReference(Control.ForegroundProperty,"Tc.Primary");
            _knowledgeProgress.SetResourceReference(Control.BackgroundProperty,"Tc.Border");
            var title=new TextBlock { Text="Wissenssuche",FontWeight=FontWeights.SemiBold,FontSize=13 };
            var close=IconButton("close","Einrichtung schließen",()=>{_knowledgePanel.Visibility=Visibility.Collapsed;return Task.CompletedTask;});
            var head=new DockPanel();DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);head.Children.Add(title);
            _knowledgeStart=AsyncButton("Einrichten",SetupKnowledgeAsync);_knowledgeStart.SetResourceReference(StyleProperty,"Tc.SecondaryButton");
            _knowledgeCancel=AsyncButton("Abbrechen",()=>{_knowledgeCancellation?.Cancel();return Task.CompletedTask;});
            _knowledgeCancel.SetResourceReference(StyleProperty,"Tc.SecondaryButton");_knowledgeCancel.Visibility=Visibility.Collapsed;
            var scan=AsyncButton("Neu suchen",async()=>{_knowledgeChecked=DateTime.MinValue;await RefreshKnowledgeAsync(true);});
            scan.SetResourceReference(StyleProperty,"Tc.SecondaryButton");
            var actions=new WrapPanel();actions.Children.Add(_knowledgeStart);actions.Children.Add(_knowledgeCancel);actions.Children.Add(scan);
            _knowledgeSources.SelectionChanged+=(sender,args)=>RenderKnowledge();
            var label=new TextBlock {Text="Installierte Hilfe",FontSize=12};label.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Muted");
            _knowledgePanel.Child=new StackPanel { Children={head,_knowledgeText,label,_knowledgeSources,_knowledgeProgress,actions} };
        }
        private void RenderKnowledge()
        {
            if(_disposed)return;
            var state=_knowledgeState;
            var language=state?.language=="de"?"Deutsch":"Englisch";
            var text=state==null?"Installierte Beckhoff-Hilfe wird gesucht …"
                :state.phase=="indexing"?"Beckhoff-Hilfe wird lokal aufbereitet. Das kann einige Minuten dauern."
                :state.phase=="validating"?"Der Suchindex wird geprüft …"
                :state.phase=="activating"?"Die geprüfte Wissenssuche wird aktiviert …"
                :state.phase=="verifying"?"Die Verbindung zur vollständigen Dokumentation wird geprüft …"
                :state.phase=="cancelled"?"Einrichtung abgebrochen. Du kannst sie später erneut starten."
                :state.phase=="error"?"Die Einrichtung konnte nicht abgeschlossen werden. Vorhandene Dokumentation bleibt erhalten. Erneut versuchen."
                :state.phase=="external"?"Eine eigene Dokumentationsquelle ist konfiguriert. Sie wird durch die automatische Einrichtung nicht ersetzt."
                :state.ready?(KnowledgeConnected()?"Wissenssuche bereit · ":"Dokumentation vorbereitet · ")+language+" · "+state.topics.ToString("N0")+" Artikel"+(state.phase=="update"?". Eine neuere installierte Hilfe ist verfügbar.":KnowledgeConnected()?".":". Die Wissenssuche verbindet …")
                :state.phase=="missing"?"Keine installierte Beckhoff-Hilfe erkannt. Installiere InfoSys und wähle danach Neu suchen."
                :"Verwende die installierte Beckhoff-Hilfe für Antworten mit Quellen. Die Dokumente werden einmal lokal aufbereitet.";
            _knowledgeText.Text=text;_knowledgeSummary.Text=text;
            _knowledgeProgress.Visibility=_knowledgeBusy?Visibility.Visible:Visibility.Collapsed;
            _knowledgeCancel.Visibility=_knowledgeBusy?Visibility.Visible:Visibility.Collapsed;
            _knowledgeCancel.IsEnabled=_knowledgeBusy&&state?.phase!="activating"&&state?.phase!="verifying"&&state?.complete!=true;
            _knowledgeSources.IsEnabled=!_knowledgeBusy;
            _knowledgeStart.IsEnabled=!_knowledgeBusy&&_knowledgeSources.SelectedItem is KnowledgeSource&&state?.phase!="external";
            _knowledgeStart.Content=state?.ready==true?"Index aktualisieren":"Einrichten";
            RenderDiagnostics();
        }
        private void ShowKnowledgeSetup(){_menuPopup.IsOpen=false;_knowledgePanel.Visibility=Visibility.Visible;RenderKnowledge();RunAction(()=>RefreshKnowledgeAsync(true));}
        private async Task RefreshKnowledgeAsync(bool force=false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(_disposed||_knowledgeChecking||_knowledgeBusy||(!force&&DateTime.UtcNow-_knowledgeChecked<TimeSpan.FromMinutes(1)))return;
            _knowledgeChecking=true;_knowledgeChecked=DateTime.UtcNow;
            try
            {
                var config=_chatConfiguration();
                if(string.IsNullOrEmpty(config.McpSettingsPath))return;
                var result=await NativeKnowledge.RunAsync(config,new {mode="status",state=config.StateRoot,mcpConfig=config.McpSettingsPath},null,CancellationToken.None);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(_disposed)return;
                _knowledgeState=result;
                var selected=result.phase=="update"?null:(_knowledgeSources.SelectedItem as KnowledgeSource)?.path;
                var sources=result.candidates??new KnowledgeSource[0];_knowledgeSources.ItemsSource=sources;
                _knowledgeSources.SelectedItem=sources.FirstOrDefault(item=>item.path==selected)
                    ??(sources.Count(item=>item.language=="de")==1?sources.First(item=>item.language=="de"):sources.Length==1?sources[0]:null);
                RenderKnowledge();RenderChatBinding();
                if(result.ready)BeginKnowledgeReconnect(true);
            }
            catch{if(!_disposed){_knowledgeState=new KnowledgeState {phase="error"};RenderKnowledge();}}
            finally{_knowledgeChecking=false;}
        }
        private async Task SetupKnowledgeAsync()
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var source=_knowledgeSources.SelectedItem as KnowledgeSource;
            if(_knowledgeBusy||_knowledgeChecking||source==null||_disposed)return;
            _knowledgeBusy=true;_knowledgeCancellation=new CancellationTokenSource();RenderKnowledge();
            try
            {
                var config=_chatConfiguration();
                var result=await NativeKnowledge.RunAsync(config,new {mode="setup",state=config.StateRoot,mcpConfig=config.McpSettingsPath,
                    source=source.path,language=source.language,acceptLocalIndexing=true},progress=>{
                        if(!_disposed){_knowledgeState=progress;RenderKnowledge();RenderChatBinding();}
                    },_knowledgeCancellation.Token);
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                if(_disposed)return;
                _knowledgeState=result;
                if(result.ready){_knowledgeConnectionKey=null;await ReconnectKnowledgeAsync();}
            }
            catch{if(!_disposed)_knowledgeState=new KnowledgeState {phase=_knowledgeCancellation.IsCancellationRequested?"cancelled":"error"};}
            finally{_knowledgeBusy=false;_knowledgeCancellation.Dispose();_knowledgeCancellation=null;if(!_disposed){RenderKnowledge();RenderChatBinding();}}
        }
        // True while automatic reconnects for a ready index remain.
        private bool KnowledgeRetrying(){return _knowledgeState?.ready==true&&_knowledgeRetries<KnowledgeRetrySeconds.Length;}
        private void RetryKnowledge()
        {
            if(_disposed||_knowledgeState?.ready!=true||!_chatAdapterReady||_chatSelection?.IsSelected!=true)return;
            var key=_knowledgeState.revision+"\0"+_chatSelection.directory+"\0"+_chatDocumentGeneration;
            if(key!=_knowledgeRetryKey){_knowledgeRetryKey=key;_knowledgeRetries=0;_knowledgeRetryAt=DateTime.MinValue;}
            var live=McpState("knowledge");
            if(live=="connected"){_knowledgeRetries=0;_knowledgeRetryAt=DateTime.MinValue;return;}
            if(live!="failed"||_knowledgeConnecting||_knowledgeBusy||_knowledgeChecking||!KnowledgeRetrying()||DateTime.UtcNow<_knowledgeRetryAt)return;
            _knowledgeRetries++;
            _knowledgeRetryAt=_knowledgeRetries<KnowledgeRetrySeconds.Length?DateTime.UtcNow.AddSeconds(KnowledgeRetrySeconds[_knowledgeRetries]):DateTime.MaxValue;
            _knowledgeConnectionKey=null;
            BeginKnowledgeReconnect(true);
        }
        private void BeginKnowledgeReconnect(bool quiet=false)
        {
            if(_knowledgeConnecting)return;
            _knowledgeConnecting=true;
            RunAction(async()=>{
                try{await ReconnectKnowledgeAsync(quiet);}
                finally{_knowledgeConnecting=false;if(!_disposed)RenderKnowledge();}
            });
        }
        private async Task ReconnectKnowledgeAsync(bool quiet=false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!_chatAdapterReady||_chatSelection?.IsSelected!=true||_knowledgeState?.ready!=true)return;
            var key=_knowledgeState.revision+"\0"+_chatSelection.directory+"\0"+_chatDocumentGeneration;
            if(key==_knowledgeConnectionKey)return;
            var directory=new JavaScriptSerializer().Serialize(_chatSelection.directory);
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatKnowledgeReload='pending';(async()=>{const h={'x-opencode-directory':encodeURIComponent("+directory+"),'x-opencode-directory-encoding':'uri'};for(const a of ['disconnect','connect']){const r=await fetch('/api/experimental/mcp/knowledge/'+a,{method:'POST',headers:h});if(r.status!==204)throw Error();}})().then(()=>window.__twinCatKnowledgeReload='ready',()=>window.__twinCatKnowledgeReload='failed');");
            var deadline=DateTime.UtcNow.AddMinutes(10);
            while(!_disposed&&DateTime.UtcNow<deadline)
            {
                var phase=await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatKnowledgeReload");
                if(phase=="\"ready\""){_knowledgeConnectionKey=key;return;}
                if(phase=="\"failed\"")break;
                await Task.Delay(200);
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!_disposed&&!quiet)Notify("Wissenssuche eingerichtet. Die Verbindung konnte noch nicht bestätigt werden.","warning","Neu verbinden",Reconnect);
        }
    }
}
