using Google.Protobuf;

using Shared;

namespace Core;

public interface IMessageHandler<TReq, TResp>
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    public Task<TResp?> HandleAsync(TReq request, IServerContext context);
}

internal interface IHandlerInvoker
{
    public void Enter();

    public void Release();

    public Task WaitForIdleAsync(CancellationToken cancellationToken);

    public Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context);
}

internal sealed class HandlerInvoker<TReq, TResp> : IHandlerInvoker
    where TReq : class, IMessage<TReq>, new()
    where TResp : class, IMessage<TResp>, new()
{
    private readonly IMessageHandler<TReq, TResp> _handler;
    private static readonly MessageParser<TReq> _reqParser = (MessageParser<TReq>)new TReq().Descriptor.Parser;
    private static readonly string _respTypeName = new TResp().Descriptor.FullName;
    private readonly TaskCompletionSource _idle = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _inFlight;

    public HandlerInvoker(IMessageHandler<TReq, TResp> handler)
    {
        _handler = handler;
    }

    public void Enter() => Interlocked.Increment(ref _inFlight);

    public void Release()
    {
        if (Interlocked.Decrement(ref _inFlight) == 0)
            _idle.TrySetResult();
    }

    public Task WaitForIdleAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref _inFlight) == 0) return Task.CompletedTask;
        return _idle.Task.WaitAsync(cancellationToken);
    }

    public async Task<(string responseType, byte[] payload, int errorCode, string errorMessage)?> Invoke(ByteString payload, IServerContext context)
    {
        TReq reqObj;
        try
        {
            reqObj = _reqParser.ParseFrom(payload);
        }
        catch (InvalidProtocolBufferException ex)
        {
            context.Log($"请求载荷解析失败 {typeof(TReq).Name}: {ex.Message}", Microsoft.Extensions.Logging.LogLevel.Warning);
            return (_respTypeName, [], ProtocolError.InvalidRequest, "invalid_request_payload");
        }

        TResp? respObj;
        try
        {
            respObj = await _handler.HandleAsync(reqObj, context);
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            context.Log($"处理器异常: {typeof(TReq).Name}: {ex}", Microsoft.Extensions.Logging.LogLevel.Error);
            return (_respTypeName, [], ProtocolError.InternalError, "internal_error");
        }

        if (respObj == null) return null;

        var responsePayload = respObj.ToByteArray();
        if (respObj is IServerResponse serverResponse && !serverResponse.Success)
        {
            var code = serverResponse.Code > 0 ? serverResponse.Code : ProtocolError.InvalidRequest;
            var message = string.IsNullOrEmpty(serverResponse.Message) ? "fail" : serverResponse.Message;
            return (_respTypeName, responsePayload, code, message);
        }

        return (_respTypeName, responsePayload, 0, string.Empty);
    }
}
