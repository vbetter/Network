using UnityEngine;
using System.Collections;
using System.Net.Sockets;
using System.Net;
using System;



public enum SocketState
{
    StateInit,
    StateConnecting,
    StateConnected,
    StateShutDown,
    StateSocketError,
}

class BufferFullExcpetion : Exception
{

    System.Object[] mObjArray;

    string formatString = "CSocket buffer is full: buffer size = {0},used start = {1} used end = {2}}";

    public BufferFullExcpetion(int bufferStart, int bufferEnd, int bufferSize)
    {
        mObjArray = new System.Object[3];
        mObjArray[0] = bufferStart;
        mObjArray[1] = bufferEnd;
        mObjArray[2] = bufferSize;
    }

    public override string ToString()
    {
        return string.Format(formatString, mObjArray);
    }
}


/// <summary>
/// 游戏中封装了的Socket类，负责发送和接受数据包
/// 被发送和接受的数据都会Copy一次，上层调用不用担心是否需要等待Socket接受或者发送完成
/// </summary>
public class CSocket
{

    Socket mSocket;                                 //底层实际发送和接收数据使用System.Net.Socket

    SocketState mState;                             //CSocekt的逻辑状态

    const int BUFFER_LEN = 1024*10;                 //缓冲区长度，定义一次发送和接收数据的总长度

    byte[] mSendBuffer = new byte[BUFFER_LEN];      //发送数据包的缓冲区
    int mSendHead = 0;                              //发送的有效数据包起始位置
    int mSendTail = 0;                              //发送的有效数据包结束位置


    byte[] m_RecvBuffer = new byte[BUFFER_LEN];     //接受数据包的缓冲区
    int mRecvHead = 0;                              //接收到的有效数据包起始位置
    int mRecvTail = 0;                              //接收到的有效数据包结束位置


    bool mIsSending = false;                        //是否在发送状态中
    bool mIsRecving = false;                        //是否在接受数据状态中

    SocketError mErrorCode;                         //错误代码

    int NetworkTypeID = -1;

    //构造函数
    public CSocket()
    {
        mState = SocketState.StateInit;
        ClearSocketErrorCode();
    }

    //获取当前Socket状态
    public SocketState GetState()
    {
        return mState;
    }

    /// <summary>
    /// 连接服务器
    /// </summary>
    /// <param name="server">服务器IP</param>
    /// <param name="port">端口号</param>
    public void ConnectSocket(string server, int port)
    {
        try
        {
			Debug.Log("ConnectSocket " + server + " " + port);
			IPAddress[] ips = Dns.GetHostAddresses(server);
			if(ips == null || ips.Length <= 0)
			{
				Debug.LogError("ConnectSocket ips == null || ips.Length <= 0");
				return;
			}
			Debug.Log("ip.AddressFamily " + ips[0].AddressFamily);
			mSocket = new Socket(ips[0].AddressFamily, SocketType.Stream, ProtocolType.Tcp);

            mSendHead = mSendTail = 0;
            mRecvHead = mRecvTail = 0;
            mIsSending = mIsRecving = false;
            mState = SocketState.StateConnecting;
            ClearSocketErrorCode();

			mSocket.BeginConnect(ips[0], port, new AsyncCallback(ConnectComplete), null);                        
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            SetErrorState(ex.SocketErrorCode);
        }

        NetworkTypeID = (int)Application.internetReachability - 1;

    }


    //Socket的Update函数，每次调用收发一次数据，先发送后接受
    public void Update()
    {
        if (mState == SocketState.StateConnected)
		{
//			Debug.Log("CSocket Update");
            ProcessSend();
            ProcessRecv();
        }
    }

    /// <summary>
    /// 主动关闭Socket，断开连接
    /// </summary>
    public void Shutdown()
	{
//		Debug.Log("Shutdown");
        mState = SocketState.StateShutDown;
        try
        {
            if (mSocket != null && mErrorCode != SocketError.NotConnected)
            {
                mSocket.Shutdown(SocketShutdown.Both);
                mSocket.Close();
            }
            mSocket = null;

        }
        catch (System.Net.Sockets.SocketException)
        {
            mState = SocketState.StateShutDown;
            mSocket = null;
        }
    }

    /// <summary>
    /// 查询是否发生过SocketError
    /// </summary>
    /// <returns></returns>
    public bool IsErrorState()
    {
        return mState == SocketState.StateSocketError;
    }

    //清除所有的Error Code
    private void ClearSocketErrorCode()
    {
        mErrorCode = System.Net.Sockets.SocketError.Success;
    }
    
    //获取当前Game Socket的异常，然后重置这个Error状态
    public SocketError SocketErrorCode
    {
        get {
            SocketError code = mErrorCode;
            return code; 
        }
    }


    /// <summary>
    /// 设置Socket的ErrorCode.
    /// 并且记录下最开始发生的SocketError
    /// </summary>
    /// <param name="errcode"></param>
    private void SetErrorState(SocketError errcode)
    {
//		Debug.Log("SetErrorState errcode " + errcode);
        if(!IsErrorState())
		{
//			Debug.Log("SetErrorState if(!IsErrorState())");
            mState = SocketState.StateSocketError;
            mErrorCode = errcode;
        }
    }


    /// <summary>
    /// 发送数据包，如果缓冲区满了，抛出异常BufferFullExcpetion
    /// 直接对SendBuffer进行数据添加的地方，如果满了，会抛出异常
    /// </summary>
    /// <param name="buf">数据源</param>
    /// <param name="iOffet">数据源偏移</param>
    /// <param name="size">发送数据长度</param>
    public void Send(byte[] buf, int iOffet, int size)
    {
//		Debug.Log("Start CSocket Send buf " + buf.Length + " iOffet " + iOffet + " size " + size);
        if (mSendTail + size > BUFFER_LEN)
        {
            System.Buffer.BlockCopy(mSendBuffer, mSendHead, mSendBuffer, 0, mSendTail - mSendHead);
            mSendTail -= mSendHead;
            mSendHead = 0;
        }

        if (mSendTail + size > BUFFER_LEN)
        {
            throw new BufferFullExcpetion(mSendHead, mSendTail, BUFFER_LEN);
        }

        System.Buffer.BlockCopy(buf, iOffet, mSendBuffer, mSendTail, size);
		mSendTail += size;
//		Debug.Log("Stop CSocket Send buf " + buf.Length + " iOffet " + iOffet + " size " + size);
    }

    /// <summary>
    /// 接受数据包，如果缓冲区满了，抛出异常BufferFullExcpetion
    /// </summary>
    /// <param name="buf">用来copy接收到数据的buffer</param>
    /// <param name="iOffset">数据偏移</param>
    /// <param name="maxSize">buffer能copy的总长</param>
    /// <returns>实际接受数据包的长度</returns>
    public int Recv(byte[] buf, int iOffset, int maxSize)
	{
//		Debug.Log("Start CSocket Recv buf " + buf.Length + " iOffet " + maxSize + " size " + maxSize);
        int iReadSize = Math.Min(maxSize, mRecvTail - mRecvHead);

        if(iReadSize != 0)
        {
            System.Buffer.BlockCopy(m_RecvBuffer, mRecvHead, buf, iOffset, iReadSize);
            mRecvHead +=  iReadSize;
        }
		
//		Debug.Log("Stop CSocket Recv buf " + buf.Length + " iOffet " + maxSize + " size " + maxSize);
        return iReadSize;
    }



    /// <summary>
    /// 在Update中调用，接收数据包，直接对RecvBuffer添加数据的地方
    /// 每次调用会整理RecvBuffer，还没有处理数据包移到buffer最前面
    /// </summary>
    private void ProcessRecv()
    {
        try
		{
//			Debug.Log("Start ProcessRecv");
            if (null != mSocket && !mIsRecving)
			{
//				Debug.Log("Start ProcessRecv if (null != mSocket && !mIsRecving)");
                if (BUFFER_LEN == mRecvTail)
				{
//					Debug.Log("Start ProcessRecv if (BUFFER_LEN == mRecvTail)");
				//	if (mRecvHead == 0)	// the buff is full. no more space to recieve any data now
				//		return ;
					
                    System.Buffer.BlockCopy(m_RecvBuffer, mRecvHead, m_RecvBuffer, 0, mRecvTail - mRecvHead);
                    mRecvTail -= mRecvHead;
                    mRecvHead = 0;
                }				
				
                mIsRecving = true;


//				Debug.Log("BeginReceive start = " + mRecvHead + " end = " +  mRecvTail + " left size = " + (BUFFER_LEN - mRecvTail));
                mSocket.BeginReceive(m_RecvBuffer, mRecvTail, BUFFER_LEN - mRecvTail, 0, new AsyncCallback(RecvCallBack), 0);
            }
        }
        catch (System.Net.Sockets.SocketException ex)
		{
//			Debug.Log("Start ProcessRecv catch (System.Net.Sockets.SocketException ex)");
            SetErrorState(ex.SocketErrorCode);
		}
//		Debug.Log("Stop ProcessRecv");
    }


    /// <summary>
    /// 在Update中调用，发送数据包
    /// </summary>
    private void ProcessSend()
	{
//		Debuger.Log("Start ProcessSend");
        try
        {
            if (null != mSocket && mSendHead != mSendTail && !mIsSending)
			{
//				Debug.Log("Start ProcessSend if (null != mSocket && mSendHead != mSendTail && !mIsSending)");
                mIsSending = true;
				mSocket.BeginSend(mSendBuffer, mSendHead, mSendTail - mSendHead, 0, new AsyncCallback(SendCallBack), 0);
//				Debug.Log("Stop ProcessSend if (null != mSocket && mSendHead != mSendTail && !mIsSending)");
            }
        }
        catch (System.Net.Sockets.SocketException ex)
		{
//			Debug.Log("ProcessSend catch (System.Net.Sockets.SocketException ex)");
            SetErrorState(ex.SocketErrorCode);
		}
//		Debug.Log("Stop ProcessSend");
     }


    //////////////////////////////////////////////////////////////////////////
    //异步回调函数，为了防止socket被关闭后执行回调，都必须非空判断

    /// <summary>
    /// 接受数据包的回调函数
    /// </summary>
    /// <param name="ar"></param>
    private void RecvCallBack(IAsyncResult ar)
	{
//		Debug.Log("RecvCallBack : " + ar);
        try
        {
            if (null != mSocket)
			{
//				Debug.Log("RecvCallBack if (null != mSocket)");
                int iRead = mSocket.EndReceive(ar);
                if (NetworkTypeID >= 0)
                {
                    mRecvByteCount[NetworkTypeID] += iRead;
                }

//                Debug.Log("Recv : " + iRead);
                mRecvTail += iRead;
                mIsRecving = false;

                //msdn If the remote host shuts down the Socket connection with the Shutdown method, and all available data has been received, the EndReceive method will complete immediately and return zero bytes.
                if (iRead == 0)
				{
//					Debug.Log("RecvCallBack if (null != mSocket) if (iRead == 0)");
                    SetErrorState(SocketError.Shutdown);
                }
            }
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            SetErrorState(ex.SocketErrorCode);
        }
    }

    /// <summary>
    /// 发送数据包的回调函数
    /// </summary>
    /// <param name="ar"></param>
    private void SendCallBack(IAsyncResult ar)
	{
//		Debug.Log("Start SendCallBack : " + ar);
        try
        {
            if (null != mSocket)
			{
//				Debug.Log("SendCallBack null != mSocket");
                int iSend = mSocket.EndSend(ar);
                if (NetworkTypeID >= 0)
                {
                    mSendByteCount[NetworkTypeID] += iSend;
                }
//                Debuger.Log("Send : " + iSend);
                mSendHead += iSend;
                mIsSending = false;
            }else
			{
//				Debug.Log("SendCallBack null == mSocket");
			}
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            SetErrorState(ex.SocketErrorCode);
		}
//		Debug.Log("Stop SendCallBack");
    }

    /// <summary>
    /// 连接成功后的回调函数
    /// </summary>
    /// <param name="ar"></param>
    private void ConnectComplete(IAsyncResult ar)
    {
        try
        {
//			Debug.LogError("Start ConnectComplete " + (ar != null));
            if (null != mSocket)
            {
//				Debug.LogError("null != mSocket");
                mSocket.EndConnect(ar);
                mState = SocketState.StateConnected;
//				Debug.LogError("ConnectComplete");
            }else
			{
//				Debug.LogError("null == mSocket");
			}
        }
        catch (System.Net.Sockets.SocketException e)
        {
            mState = SocketState.StateSocketError;
            mErrorCode = (SocketError)e.ErrorCode;
//			Debug.LogError(e.ToString());
		}
//		Debug.LogError("Stop ConnectComplete");
    }

    static int[] mSendByteCount = new int[2];
    static int[] mRecvByteCount = new int[2];

    public static void ResetStatistics()
    {
        for( int i = 0; i < 2; ++i )
        {
            mSendByteCount[i] = 0;
            mRecvByteCount[i] = 0;
        }
    }

    public static int GetSendStatistics( NetworkReachability networkType )
    {
        if (networkType == NetworkReachability.NotReachable)
        {
            return 0;
        }

        return mSendByteCount[(int)networkType-1];
    }


    public static int GetRecvStatistics( NetworkReachability networkType )
    {
        if (networkType == NetworkReachability.NotReachable)
        {
            return 0;
        }

        return mRecvByteCount[(int)networkType - 1];
    }


}