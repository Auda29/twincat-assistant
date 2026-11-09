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
                &&McpState("knowledge")=="connected";
        }

        private string KnowledgeConnectionText()
        {
            if(_chatSelection?.IsSelected!=true||_chatBinding?.IsGeneral==true)return HostText.Get("H210");
            if(_knowledgeConnecting)return HostText.Get("H211");
            switch(McpState("knowledge"))
            {
                case "connected": return HostText.Get("H212");
                case "pending": return HostText.Get("H213");
                case "failed": return KnowledgeRetrying()?HostText.Get("H214"):HostText.Get("H215");
                case "disabled": return HostText.Get("H216");
                case "needs_auth": return HostText.Get("H217");
                default: return HostText.Get("H218");
            }
        }

        private void BuildKnowledge()
        {
            _knowledgePanel.SetResourceReference(Border.BackgroundProperty,"Tc.Surface");
            _knowledgePanel.SetResourceReference(Border.BorderBrushProperty,"Tc.Border");
            _knowledgeText.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Foreground");
            _knowledgeSummary.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Muted");
            _knowledgeProgress.SetResourceReference(Control.ForegroundProperty,"Tc.Primary");
            _knowledgeProgress.SetResourceReference(Control.BackgroundProperty,"Tc.Border");
            var title=new TextBlock { Text=HostText.Get("H120"),FontWeight=FontWeights.SemiBold,FontSize=13 };
            var close=IconButton("close",HostText.Get("H219"),()=>{_knowledgePanel.Visibility=Visibility.Collapsed;return Task.CompletedTask;});
            var head=new DockPanel();DockPanel.SetDock(close,Dock.Right);head.Children.Add(close);head.Children.Add(title);
            _knowledgeStart=AsyncButton(HostText.Get("H097"),SetupKnowledgeAsync);_knowledgeStart.SetResourceReference(StyleProperty,"Tc.SecondaryButton");
            _knowledgeCancel=AsyncButton(HostText.Get("H220"),()=>{_knowledgeCancellation?.Cancel();return Task.CompletedTask;});
            _knowledgeCancel.SetResourceReference(StyleProperty,"Tc.SecondaryButton");_knowledgeCancel.Visibility=Visibility.Collapsed;
            var scan=AsyncButton(HostText.Get("H221"),async()=>{_knowledgeChecked=DateTime.MinValue;await RefreshKnowledgeAsync(true);});
            scan.SetResourceReference(StyleProperty,"Tc.SecondaryButton");
            var actions=new WrapPanel();actions.Children.Add(_knowledgeStart);actions.Children.Add(_knowledgeCancel);actions.Children.Add(scan);
            _knowledgeSources.SelectionChanged+=(sender,args)=>RenderKnowledge();
            var label=new TextBlock {Text=HostText.Get("H222"),FontSize=12};label.SetResourceReference(TextBlock.ForegroundProperty,"Tc.Muted");
            _knowledgePanel.Child=new StackPanel { Children={head,_knowledgeText,label,_knowledgeSources,_knowledgeProgress,actions} };
        }
        private void RenderKnowledge()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_disposed)
                    return;
                var state = _knowledgeState;
                var language = state?.language == "de" ? HostText.Get("H110") : HostText.Get("H111");
                var text = state == null ? HostText.Get("H223") : state.phase == "indexing" ? HostText.Get("H224") : state.phase == "validating" ? HostText.Get("H225") : state.phase == "activating" ? HostText.Get("H226") : state.phase == "verifying" ? HostText.Get("H227") : state.phase == "cancelled" ? HostText.Get("H228") : state.phase == "error" ? HostText.Get("H229") : state.phase == "external" ? HostText.Get("H230") : state.ready ? HostText.Format("H231", new object[] { language, state.topics.ToString("N0"), (state.phase == "update" ? HostText.Get("H232") : ""), Environment.NewLine, KnowledgeConnectionText() }) : state.phase == "missing" ? HostText.Get("H233") : HostText.Get("H234");
                _knowledgeText.Text = text;
                _knowledgeSummary.Text = text;
                _knowledgeProgress.Visibility = _knowledgeBusy ? Visibility.Visible : Visibility.Collapsed;
                _knowledgeCancel.Visibility = _knowledgeBusy ? Visibility.Visible : Visibility.Collapsed;
                _knowledgeCancel.IsEnabled = _knowledgeBusy && state?.phase != "activating" && state?.phase != "verifying" && state?.complete != true;
                _knowledgeSources.IsEnabled = !_knowledgeBusy;
                _knowledgeStart.IsEnabled = !_knowledgeBusy && _knowledgeSources.SelectedItem is KnowledgeSource && state?.phase != "external";
                _knowledgeStart.Content = state?.ready == true ? HostText.Get("H235") : HostText.Get("H097");
                RenderDiagnostics();
            }
            finally
            {
                ApplyLanguageBindings();
            }
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
                if(result.ready)RetryKnowledge();
            }
            catch{if(!_disposed){if(_knowledgeState?.ready!=true)_knowledgeState=new KnowledgeState {phase="error"};RenderKnowledge();}}
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
                if(result.ready){_knowledgeConnectionKey=null;await ReconnectKnowledgeAsync(force:true);}
            }
            catch{if(!_disposed)_knowledgeState=new KnowledgeState {phase=_knowledgeCancellation.IsCancellationRequested?"cancelled":"error"};}
            finally{_knowledgeBusy=false;_knowledgeCancellation.Dispose();_knowledgeCancellation=null;if(!_disposed){RenderKnowledge();RenderChatBinding();}}
        }
        // True while automatic reconnects for a ready index remain.
        private bool KnowledgeRetrying(){return _knowledgeState?.ready==true&&_knowledgeRetries<KnowledgeRetrySeconds.Length;}
        private void RetryKnowledge()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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
        private void BeginKnowledgeReconnect(bool quiet=false,bool force=false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if(_knowledgeConnecting)return;
            _knowledgeConnecting=true;
            RenderKnowledge();
            RunAction(async()=>{
                try{await ReconnectKnowledgeAsync(quiet,force);}
                finally{_knowledgeConnecting=false;if(!_disposed)RenderKnowledge();}
            });
        }
        private async Task ReconnectKnowledgeAsync(bool quiet=false,bool force=false)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!_chatAdapterReady||_chatSelection?.IsSelected!=true||_knowledgeState?.ready!=true)return;
            var key=_knowledgeState.revision+"\0"+_chatSelection.directory+"\0"+_chatDocumentGeneration;
            if(!force&&(McpState("knowledge")=="connected"||key==_knowledgeConnectionKey))return;
            var generation=_chatDocumentGeneration;
            var requestId=Guid.NewGuid().ToString("N");
            var directory=new JavaScriptSerializer().Serialize(_chatSelection.directory);
            await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatKnowledgeReload={id:'"+requestId+"',phase:'pending'};(async()=>{const h={'x-opencode-directory':encodeURIComponent("+directory+"),'x-opencode-directory-encoding':'uri'};for(const a of ['disconnect','connect']){const r=await fetch('/api/experimental/mcp/knowledge/'+a,{method:'POST',headers:h});if(r.status!==204)throw Error();}})().then(()=>{if(window.__twinCatKnowledgeReload?.id==='"+requestId+"')window.__twinCatKnowledgeReload.phase='ready';},()=>{if(window.__twinCatKnowledgeReload?.id==='"+requestId+"')window.__twinCatKnowledgeReload.phase='failed';});");
            var deadline=DateTime.UtcNow.AddMinutes(10);
            while(!_disposed&&generation==_chatDocumentGeneration&&DateTime.UtcNow<deadline)
            {
                if(key!=_knowledgeState?.revision+"\0"+_chatSelection?.directory+"\0"+_chatDocumentGeneration)return;
                var phase=await _browser.CoreWebView2.ExecuteScriptAsync("window.__twinCatKnowledgeReload?.id==='"+requestId+"'?window.__twinCatKnowledgeReload.phase:null");
                if(phase=="\"ready\""){_knowledgeConnectionKey=key;return;}
                if(phase=="\"failed\"")break;
                await Task.Delay(200);
            }
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if(!_disposed&&generation==_chatDocumentGeneration&&!quiet)Notify(HostText.Get("H236"),"warning",HostText.Get("H083"),()=>BeginKnowledgeReconnect(force:true));
        }
    }
}
