using Microsoft.Extensions.Options;
using Secs4Net;
using Serilog;
using System;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace SecsUtil;

public enum ConnectionMode
{
    Active,
    Passive
}

public class TimerStatus
{
    public bool Active { get; set; }
    public int Remaining { get; set; }
    public int Total { get; set; }
    public double Progress => Total > 0 ? ((Total - Remaining) * 100.0 / Total) : 0;
}

public class ConnectionManager : IDisposable
{
    private static readonly ILogger _logger = Log.ForContext<ConnectionManager>();
    
    private ISecsConnection? _connection;
    private SecsGem? _secsGem;
    private IOptions<SecsGemOptions>? _options;
    private SecsGemLogger? _gemLogger;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _messageReceiveTask;
    
    public ConnectionMode Mode { get; private set; }
    public ConnectionState State => _connection?.State ?? ConnectionState.NotConnected;
    public bool IsConnected => State == ConnectionState.Selected;
    
    public event EventHandler<ConnectionState>? ConnectionChanged;
    public event EventHandler<SecsMessageEventArgs>? MessageReceived;
    public event EventHandler<RawDataEventArgs>? RawDataReceived;
    public Func<SecsMessage, SecsMessage?>? AutoReplyFactory { get; set; }
    
    public ushort? SessionId { get; private set; }
    public string PeerAddress { get; private set; } = string.Empty;
    
    private readonly TimerStatus _t3Status = new();
    private readonly TimerStatus _t5Status = new();
    private readonly TimerStatus _t7Status = new();
    private readonly TimerStatus _t8Status = new();
    
    public bool T3Active => _t3Status.Active;
    public int T3Remaining => _t3Status.Remaining;
    public double T3Progress => _t3Status.Progress;
    
    public bool T5Active => _t5Status.Active;
    public int T5Remaining => _t5Status.Remaining;
    public double T5Progress => _t5Status.Progress;
    
    public bool T7Active => _t7Status.Active;
    public int T7Remaining => _t7Status.Remaining;
    public double T7Progress => _t7Status.Progress;
    
    public bool T8Active => _t8Status.Active;
    public int T8Remaining => _t8Status.Remaining;
    public double T8Progress => _t8Status.Progress;
    
    public async Task ConnectAsync(ConnectionMode mode, string ipAddress, int port, ushort deviceId, 
        int t3 = 45000, int t5 = 10000, int t6 = 5000, int t7 = 10000, int t8 = 5000)
    {
        if (_connection != null)
        {
            await DisconnectAsync();
        }

        Mode = mode;
        PeerAddress = $"{ipAddress}:{port}";
        
        _cancellationTokenSource = new CancellationTokenSource();
        
        _options = Options.Create(new SecsGemOptions
        {
            IsActive = mode == ConnectionMode.Active,
            IpAddress = ipAddress,
            Port = port,
            DeviceId = deviceId,
            T3 = t3,
            T5 = t5,
            T6 = t6,
            T7 = t7,
            T8 = t8
        });

        _gemLogger = new SecsGemLogger(this);
        
        try
        {
            _connection = new HsmsConnection(_options, _gemLogger);
            _connection.ConnectionChanged += OnConnectionChanged;
            
            _secsGem = new SecsGem(_options, _connection, _gemLogger);
            
            _connection.Start(_cancellationTokenSource.Token);
            
            _messageReceiveTask = ReceiveMessagesAsync();
            
            _logger.Information($"Started {mode} connection to {ipAddress}:{port} (DeviceId: {deviceId})");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to connect in {mode} mode");
            throw;
        }
    }
    
    private async Task ReceiveMessagesAsync()
    {
        if (_secsGem == null || _cancellationTokenSource == null)
            return;
            
        try
        {
            await foreach (var messageWrapper in _secsGem.GetPrimaryMessageAsync(_cancellationTokenSource.Token))
            {
                var primaryMsg = messageWrapper.PrimaryMessage;
                _logger.Debug($"Received primary message: S{primaryMsg.S}F{primaryMsg.F}");

                if (primaryMsg.F % 2 == 1)
                {
                    await SendAutoReplyAsync(messageWrapper);
                }
            }
        }
        catch (OperationCanceledException)
        {
            _logger.Debug("Message receive loop canceled");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error receiving messages");
        }
    }
    
    private async Task SendAutoReplyAsync(PrimaryMessageWrapper messageWrapper)
    {
        try
        {
            var requestMessage = messageWrapper.PrimaryMessage;
                
            var replyMessage = AutoReplyFactory?.Invoke(requestMessage);
            if (replyMessage == null)
                return;
            
            _logger.Information($"Auto-replying S{requestMessage.S}F{requestMessage.F} -> S{replyMessage.S}F{replyMessage.F}");
            
            await messageWrapper.TryReplyAsync(replyMessage);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, $"Failed to send auto-reply for S{messageWrapper.PrimaryMessage.S}F{messageWrapper.PrimaryMessage.F}");
        }
    }

    public void Connect(ConnectionMode mode, string ipAddress, int port, byte deviceId,
        int t3 = 45000, int t5 = 10000, int t6 = 5000, int t7 = 10000)
    {
        ConnectAsync(mode, ipAddress, port, deviceId, t3, t5, t6, t7).Wait();
    }

    public async Task DisconnectAsync()
    {
        _cancellationTokenSource?.Cancel();
        
        _cancellationTokenSource?.Dispose();
        _cancellationTokenSource = null;
        
        _messageReceiveTask = null;
        
        if (_connection != null)
        {
            _connection.ConnectionChanged -= OnConnectionChanged;
            
            if (_connection is IAsyncDisposable asyncDisposable)
            {
                await asyncDisposable.DisposeAsync();
            }
            else
            {
                (_connection as IDisposable)?.Dispose();
            }
            
            _connection = null;
        }
        
        _secsGem?.Dispose();
        _secsGem = null;
        
        PeerAddress = string.Empty;
        _logger.Information("Disconnected");
        
        ConnectionChanged?.Invoke(this, ConnectionState.NotConnected);
    }

    public void Disconnect()
    {
        DisconnectAsync().Wait();
    }

    public async Task<SecsMessage?> SendMessageAsync(SecsMessage message, CancellationToken cancellation = default)
    {
        if (_secsGem == null || !IsConnected)
        {
            throw new InvalidOperationException("Not connected");
        }
        
        _logger.Debug($"Sending message: {message}");
        var response = await _secsGem.SendAsync(message, cancellation);
        _logger.Debug($"Received response: {response}");
        return response;
    }

    private void OnConnectionChanged(object? sender, ConnectionState state)
    {
        _logger.Information($"Connection state changed: {state}");
        
        if (state == ConnectionState.Selected)
        {
            StartTimerSimulation();
        }
        
        ConnectionChanged?.Invoke(this, state);
    }
    
    private void StartTimerSimulation()
    {
        _t3Status.Active = false;
        _t5Status.Active = false;
        _t7Status.Active = false;
        _t8Status.Active = false;
        
        if (_options != null)
        {
            _t3Status.Total = _options.Value.T3;
            _t3Status.Remaining = _options.Value.T3;
            
            _t5Status.Total = _options.Value.T5;
            _t5Status.Remaining = _options.Value.T5;
            
            _t7Status.Total = _options.Value.T7;
            _t7Status.Remaining = _options.Value.T7;
            
            _t8Status.Total = _options.Value.T8;
            _t8Status.Remaining = _options.Value.T8;
        }
    }

    public void UpdateTimerStatus()
    {
        if (_options == null)
        {
            _t3Status.Active = false;
            _t5Status.Active = false;
            _t7Status.Active = false;
            _t8Status.Active = false;
            return;
        }
        
        var now = DateTime.Now.Ticks;
        
        UpdateTimer(_t3Status, _options.Value.T3);
        UpdateTimer(_t5Status, _options.Value.T5);
        UpdateTimer(_t7Status, _options.Value.T7);
        UpdateTimer(_t8Status, _options.Value.T8);
    }
    
    private void UpdateTimer(TimerStatus status, int totalMs)
    {
        status.Total = totalMs;
        
        if (!status.Active)
        {
            status.Remaining = totalMs;
        }
        else
        {
            status.Remaining = Math.Max(0, status.Remaining - 100);
            if (status.Remaining <= 0)
            {
                status.Active = false;
                status.Remaining = totalMs;
            }
        }
    }

    public void Dispose()
    {
        _secsGem?.Dispose();
        (_connection as IDisposable)?.Dispose();
    }

    private class SecsGemLogger : ISecsGemLogger
    {
        private readonly ConnectionManager _manager;
        
        public SecsGemLogger(ConnectionManager manager)
        {
            _manager = manager;
        }

        public void MessageIn(SecsMessage msg, int id)
        {
            _logger.Debug($"RX [{id:X8}]: {msg}");
            _manager.MessageReceived?.Invoke(_manager, new SecsMessageEventArgs(msg, id, true));
        }

        public void MessageOut(SecsMessage msg, int id)
        {
            _logger.Debug($"TX [{id:X8}]: {msg}");
            _manager.MessageReceived?.Invoke(_manager, new SecsMessageEventArgs(msg, id, false));
        }

        public void Debug(string msg) => _logger.Debug(msg);
        public void Info(string msg) => _logger.Information(msg);
        public void Warning(string msg) => _logger.Warning(msg);
        public void Error(string msg, SecsMessage? message, Exception? ex) => _logger.Error(ex, msg);
    }
}

public class SecsMessageEventArgs : EventArgs
{
    public SecsMessage Message { get; }
    public int MessageId { get; }
    public bool IsReceived { get; }
    
    public SecsMessageEventArgs(SecsMessage message, int messageId, bool isReceived)
    {
        Message = message;
        MessageId = messageId;
        IsReceived = isReceived;
    }
}

public class RawDataEventArgs : EventArgs
{
    public byte[] Data { get; }
    public bool IsReceived { get; }
    
    public RawDataEventArgs(byte[] data, bool isReceived)
    {
        Data = data;
        IsReceived = isReceived;
    }
}
