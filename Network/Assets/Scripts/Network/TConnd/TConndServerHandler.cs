using System.Collections;
using System;
using System.Runtime.InteropServices;
using tsf4g_tdr_csharp;
using tqqapi;
using UnityEngine;

public delegate void TConndSocketEventDelegate();
public delegate void TConndSocketErrorHandleDelegate(int errorCode);

/// <summary>
/// TConnd服务器的句柄，每个服务器只有一个Socket连接
/// 提供数据包的序列化反序列化
/// 连接中回调事件的注册
/// </summary>
public class TConndServerHandler
{
    public delegate System.Object UnpackDelegate(byte[] data, int buffLength,ref int parsedLength);

    /// <summary>
    /// TConndHandler逻辑状态
    /// </summary>
    public enum TConndServerState
    {
        StateInit,                  //已经初始化状态，可以准备一次连接   
        StateConnected,             //TConnd连接成功
        StateClosed                 //关闭
    }


    private CSocket mSocket;                                    //底层使用的System.Net.Socket
    TConndServerState mServerState;                             //Socket状态
    public const int TdrPackageBufferLength = 1024*10;          //单次数据包最大的长度
    private byte[]  mSendBuffer;                                //发送数据的缓冲区，有意义的数据从第一个字节开始

    private int     mRecvBytes;                                 //获取数据的偏移
    private byte[]  mRecvBuffer;                                //接受数据的缓冲区，有意义的数据从第一个字节开始
    private int mRecvBufferLen = TdrPackageBufferLength;        //收包缓冲区的默认长度
    
    int             m_iSequence;                                //数据包的时序


    ///回调函数
    TConndSocketEventDelegate mConnectedSuccessDelegate;        //连接成功的回调
    TConndSocketErrorHandleDelegate mErrorHandleDelegate;       //连接出错回调


    //超时处理
    float mConnectingStartTime = 0;                             //建立连接后处于Connecting的时间
    float mConnectTimeout = 5;                                  //超时时间


    /// <summary>
    /// 发送消息的序列
    /// </summary>
    /// <returns></returns>
    int GetSequence()
    {
        return m_iSequence++;
    }

    public TConndServerHandler()
    {
        mRecvBuffer = new byte[mRecvBufferLen];
        mSendBuffer = new byte[TdrPackageBufferLength];
        mRecvBytes = 0;

        System.Random r = new System.Random((int)DateTime.Now.Ticks);
        m_iSequence = (int)r.Next();
    }

    #region 流程处理

    /// <summary>
    /// 连接服务器
    /// </summary>
    /// <param name="serverName"></param>
    /// <param name="port"></param>
    /// <param name="dataType"></param>
    public void Connect(string serverName, int port,
        TConndSocketEventDelegate connectedDelegate, TConndSocketErrorHandleDelegate errorHandleDelegate,
        float connectTimeout = 5)
    {
        mServerState = TConndServerState.StateInit;
        mConnectedSuccessDelegate = connectedDelegate;
        mErrorHandleDelegate = errorHandleDelegate;
        mConnectTimeout = connectTimeout;
        mSocket = new CSocket();
        mSocket.ConnectSocket(serverName, port);
        mConnectingStartTime = UnityEngine.Time.realtimeSinceStartup;
    }


    /// <summary>
    /// 关闭和服务器的连接
    /// </summary>
    public void Close()
    {
        if (mSocket != null)
            mSocket.Shutdown();

        mSocket = null;

        mSendBuffer = null;
        mRecvBuffer = null;
        mRecvBytes = 0;
        mServerState = TConndServerState.StateClosed;
    }

    /// <summary>
    /// 更新函数，应该每帧被调用
    /// </summary>
    public void Update()
    {
//		Debug.LogError("TConndServerHandler Update");
        if (mSocket != null)
        {

            mSocket.Update();

            if (mSocket.SocketErrorCode != System.Net.Sockets.SocketError.Success)
            {
                mErrorHandleDelegate((int)mSocket.SocketErrorCode);
            }

            switch (mServerState)
            {
                case TConndServerState.StateInit:
				{
                    SocketState state = mSocket.GetState();
                    if (state == SocketState.StateConnected)
					{
                        mServerState = TConndServerState.StateConnected;
                        if (mConnectedSuccessDelegate != null)
                        {
                            mConnectedSuccessDelegate();
                        }
					}
                    else if (state == SocketState.StateConnecting)
                    {
                        if (Time.realtimeSinceStartup - mConnectingStartTime > mConnectTimeout)
                        {
                            mServerState = TConndServerState.StateClosed;
                            mSocket.Shutdown();
                            //清理Socket不再接受数据和处理任何事件了
                            mSocket = null;
                            if (mErrorHandleDelegate != null)
                            {
                                mErrorHandleDelegate((int)(System.Net.Sockets.SocketError.TimedOut));
                            }
                            return;
                        }
                    }
					else if (state == SocketState.StateSocketError)
					{
                        int errorCode = (int)mSocket.SocketErrorCode;
						mServerState = TConndServerState.StateClosed;
                        mSocket.Shutdown();
                        //清理Socket不再接受数据和处理任何事件了
                        mSocket = null;
                        if (mErrorHandleDelegate != null)
                        {
                            mErrorHandleDelegate(errorCode);
                        }
					}
                    break;
				}//end TConndServerState.StateInit: 
                case TConndServerState.StateConnected :
                {
                    if (mSocket.GetState() == SocketState.StateSocketError)
					{
                        int errorCode = (int)mSocket.SocketErrorCode;
						mServerState = TConndServerState.StateClosed;
                        mSocket.Shutdown();
                        //清理Socket不再接受数据和处理任何事件了
                        mSocket = null;
                        if (mErrorHandleDelegate != null)
                        {
                            mErrorHandleDelegate(errorCode);
                        }
					}
                    break;
                }
                default:
                    break;
            }//end switch
        }
    }

    /// <summary>
    /// 是否连接成功
    /// </summary>
    /// <returns></returns>
    public bool IsConnected()
    {
        //return mServerState == TConndServerState.StateConnected;
        if ( mSocket == null )
        {
            return false;
        }
        return mSocket.GetState() == SocketState.StateConnected;
    }


    public bool IsShutDown()
    {
        return mSocket == null || mSocket.GetState() == SocketState.StateShutDown;
    }

    /// <summary>
    /// 获取出错代码
    /// </summary>
    /// <returns></returns>
    public System.Net.Sockets.SocketError GetSocketError()
    {
        if (mSocket != null)
        {
            return mSocket.SocketErrorCode;
        }
        return System.Net.Sockets.SocketError.Success;
    }

    #endregion


    /// <summary>
    /// 发送字节数据到服务器
    /// </summary>
    /// <param name="data"></param>
    /// <param name="length"></param>
    /// <returns></returns>
    public int Send(byte[] data,int dataLength)
    {
        if (mSocket == null || dataLength <= 0)
        {
            return 0;
        }


        int sendLength = dataLength;

        //System.Buffer.BlockCopy(data, 0, mSendBuffer, 0, dataLength);

        //mSocket.Send(mSendBuffer, 0, sendLength);
        mSocket.Send(data, 0, dataLength);

        return sendLength;
    }

    /// <summary>
    /// 解析协议数据，将收到的网络数据组装成一个协议数据
    /// </summary>
    /// <param name="unpackFunc">用来解析数据的代码</param>
    /// <returns></returns>
    public System.Object ParseProtocolData(ref UnpackDelegate unpackFunc)
    {
        if (mSocket == null)
        {
            return null;
        }

        if ( mRecvBytes == mRecvBufferLen )
        {//缓冲区已经满了，自增长

            mRecvBufferLen += TdrPackageBufferLength;
            byte[] new_buffer = new byte[mRecvBufferLen];
            System.Buffer.BlockCopy(mRecvBuffer, 0, new_buffer, 0, mRecvBytes);

            mRecvBuffer = new_buffer;
            Debuger.Log("RecvBuffer auto grow : " + mRecvBufferLen);
        }

        int iRead = mSocket.Recv(mRecvBuffer, mRecvBytes, mRecvBufferLen - mRecvBytes);

        if (iRead == 0 && mRecvBytes == 0)
        {
            return null;
        }

        mRecvBytes += iRead;

        System.Object obj = null;

        int iPkgLen = 0;
        obj = unpackFunc(mRecvBuffer, mRecvBytes, ref iPkgLen);
        
        if (iPkgLen > 0)
        {
            System.Buffer.BlockCopy(mRecvBuffer, iPkgLen, mRecvBuffer, 0, mRecvBufferLen - iPkgLen);
            mRecvBytes -= iPkgLen;
        }

        return obj;
    }


    // 计算流量
    public void ResetStatistics()
    {
        CSocket.ResetStatistics();
    }

    public int GetSendStatistics( NetworkReachability networkType )
    {
        return CSocket.GetSendStatistics(networkType);
    }


    public int GetRecvStatistics( NetworkReachability networkType )
    {
        return CSocket.GetRecvStatistics(networkType);
    }

    public int GetStatistics(NetworkReachability networkType)
    {
        return CSocket.GetSendStatistics(networkType) + CSocket.GetRecvStatistics(networkType);
    }
}


