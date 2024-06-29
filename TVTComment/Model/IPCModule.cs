using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TVTComment.Model
{
    public enum IPCModuleState
    {
        BeforeConnect,
        Connected,
        Receiving,
        Disposing,
        Disposed
    }

    public enum IPCModuleDisposeReason
    {
        DisposeCalled,
        SendFailure,
        ReceiveFailure,
        ConnectionTerminated,
        UnexpectedError,
    }

    class IPCModule : IDisposable
    {
        public class ConnectException : Exception
        {
            public ConnectException() { }
            public ConnectException(string message) : base(message) { }
            public ConnectException(string message, Exception inner) : base(message, inner) { }
        }

        private IPC.IPCTunnel tunnel;

        private readonly SemaphoreSlim disposeLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private Task receiveTask;
        private CancellationTokenSource cancel;

        private volatile IPCModuleState _state;
        public IPCModuleState State { get { return _state; } private set { _state = value; } }
        public IPCModuleDisposeReason? DisposeReason { get; private set; } = null;

        public IPC.IPCTunnel Tunnel { get { return tunnel; } }
        public SynchronizationContext MessageReceivedSynchronizationContext { get; }

        public delegate void MessageReceivedEventHandler(IPC.IPCMessage.IIPCMessage message);
        /// <summary>
        /// メッセージを受信した時に<see cref="MessageReceivedSynchronizationContext"/>上で呼ばれる
        /// CloseIPCMessageを受信した時はこのイベントの発火し、以後一切受信を停止する
        /// </summary>
        public event MessageReceivedEventHandler MessageReceived;

        public delegate void DisposedEventHandler(Exception exception);
        /// <summary>
        /// Disposeした時に<see cref="MessageReceivedSynchronizationContext"/>上で呼ばれる
        /// </summary>
        public event DisposedEventHandler Disposed;

        /// <summary>
        /// <see cref="IPCModule"/>を初期化する
        /// </summary>
        /// <param name="messageReceivedSynchronizationContext"><see cref="MessageReceived"/>を呼び出す同期コンテキスト
        /// <para><seealso cref="SynchronizationContext.Send"/>や<seealso cref="SynchronizationContext.Post"/>された処理を排他的に実行するものである必要がある</para></param>
        public IPCModule(string sendPipeName, string receivePipeName, SynchronizationContext messageReceivedSynchronizationContext)
        {
            State = IPCModuleState.BeforeConnect;
            tunnel = new IPC.IPCTunnel(sendPipeName, receivePipeName);
            MessageReceivedSynchronizationContext = messageReceivedSynchronizationContext;
        }

        /// <summary>
        /// 接続する
        /// <see cref="ConnectException"/>が投げられた時は再接続を試せる
        /// それ以外の例外が投げられた時はdisposeされる
        /// </summary>
        /// <exception cref="ConnectException"/>
        public async Task Connect()
        {
            var state = State;
            if (state != IPCModuleState.BeforeConnect)
                throw new InvalidOperationException($"Cannot call this method on current state. Current state is '{state}'");
            try
            {
                await tunnel.Connect(new CancellationTokenSource(5000).Token).ConfigureAwait(false);
                State = IPCModuleState.Connected;
            }
            catch (IOException e)
            {
                throw new ConnectException("Failed to connect by IOError", e);
            }
            catch (OperationCanceledException e)
            {
                throw new ConnectException("Failed to connect by timeout", e);
            }
            catch (Exception e)
            {
                //不明な例外が投げられた時は以降操作できない
                await Dispose(IPCModuleDisposeReason.UnexpectedError, e);
                throw;
            }
        }

        /// <summary>
        /// 受信スレッドを開始する
        /// </summary>
        public void StartReceiving()
        {
            var state = State;
            if (state != IPCModuleState.Connected)
                throw new InvalidOperationException($"Cannot call this method on current state. Current state is '{state}'");

            cancel = new CancellationTokenSource();
            receiveTask = ReceiveLoop();
        }

        /// <summary>
        /// メッセージを送信する(スレッドセーフ)
        /// IOエラーが起きた場合このメソッド自体は例外を出さずに勝手にDisposeする
        /// </summary>
        /// <param name="message">送信するメッセージ</param>
        public async Task Send(IPC.IPCMessage.IIPCMessage message)
        {
            var state = State;
            if (state != IPCModuleState.Connected && state != IPCModuleState.Receiving)
                throw new InvalidOperationException($"Cannot call this method on current state. Current state is '{state}'");

            await sendLock.WaitAsync();
            try
            {
                await tunnel.Send(message, new CancellationTokenSource(1000).Token).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                await Dispose(IPCModuleDisposeReason.SendFailure, e).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                await Dispose(IPCModuleDisposeReason.SendFailure).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await Dispose(IPCModuleDisposeReason.UnexpectedError, e).ConfigureAwait(false);
                throw;
            }
            finally
            {
                sendLock.Release();
            }
            //Debug.WriteLine("Sent: "+message);
        }

        private async Task ReceiveLoop()
        {
            State = IPCModuleState.Receiving;
            try
            {
                while (!cancel.IsCancellationRequested)
                {
                    IPC.IPCMessage.IIPCMessage msg;
                    try
                    {
                        msg = await tunnel.Receive(cancel.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    //Debug.WriteLine("Received: " + msg);
                    MessageReceivedSynchronizationContext.Post((state) =>
                    {
                        if (cancel != null && !cancel.IsCancellationRequested)
                            MessageReceived?.Invoke(msg);
                    }, null);
                }
                cancel = null;
            }
            catch (EndOfStreamException)
            {
                await Dispose(IPCModuleDisposeReason.ConnectionTerminated).ConfigureAwait(false);
            }
            catch (IOException e)
            {
                await Dispose(IPCModuleDisposeReason.ReceiveFailure, e).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await Dispose(IPCModuleDisposeReason.UnexpectedError, e).ConfigureAwait(false);
            }
        }

        private async Task Dispose(IPCModuleDisposeReason reason, Exception e = null)
        {
            await disposeLock.WaitAsync().ConfigureAwait(false);
            try
            {
                var state = State;
                if (state == IPCModuleState.Disposing || state == IPCModuleState.Disposed)
                    return;
                State = IPCModuleState.Disposing;
                DisposeReason = reason;
            }
            finally
            {
                disposeLock.Release();
            }

            cancel?.Cancel();

            if (receiveTask != null)
            {
                try
                {
                    await receiveTask.ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    //切断されていても無視
                }
                catch (IOException)
                {
                    //IOエラーがあっても無視
                }
                finally
                {
                    receiveTask = null;
                }
            }

            tunnel?.Dispose();
            tunnel = null;

            State = IPCModuleState.Disposed;

            MessageReceivedSynchronizationContext.Post(_ => Disposed?.Invoke(e), null);
        }

        public void Dispose()
        {
            Dispose(IPCModuleDisposeReason.DisposeCalled).Wait();
        }
    }
}
