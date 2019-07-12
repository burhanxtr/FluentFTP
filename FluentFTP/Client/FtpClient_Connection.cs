using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Reflection;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Globalization;
using System.Security.Authentication;
using System.Net;
using FluentFTP.Proxy;
#if !CORE
using System.Web;
#endif

#if (CORE || NETFX)
using System.Threading;
#endif
#if ASYNC
using System.Threading.Tasks;
#endif

namespace FluentFTP {

	/// <summary>
	/// A connection to a single FTP server. Interacts with any FTP/FTPS server and provides a high-level and low-level API to work with files and folders.
	/// 
	/// Debugging problems with FTP is much easier when you enable logging. See the FAQ on our Github project page for more info.
	/// </summary>
	public partial class FtpClient : IDisposable {
		
		#region Constructor / Destructor

		/// <summary>
		/// Creates a new instance of an FTP Client.
		/// </summary>
		public FtpClient() {
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of an FTP Client, with the given host.
		/// </summary>
		public FtpClient(string host) {
			Host = host;
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of an FTP Client, with the given host and credentials.
		/// </summary>
		public FtpClient(string host, NetworkCredential credentials) {
			Host = host;
			Credentials = credentials;
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of an FTP Client, with the given host, port and credentials.
		/// </summary>
		public FtpClient(string host, int port, NetworkCredential credentials) {
			Host = host;
			Port = port;
			Credentials = credentials;
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of an FTP Client, with the given host, username and password.
		/// </summary>
		public FtpClient(string host, string user, string pass) {
			Host = host;
			Credentials = new NetworkCredential(user, pass);
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of an FTP Client, with the given host, port, username and password.
		/// </summary>
		public FtpClient(string host, int port, string user, string pass) {
			Host = host;
			Port = port;
			Credentials = new NetworkCredential(user, pass);
			m_listParser = new FtpListParser(this);
		}

		/// <summary>
		/// Creates a new instance of this class. Useful in FTP proxy classes.
		/// </summary>
		/// <returns></returns>
		protected virtual FtpClient Create() {
			return new FtpClient();
		}

		/// <summary>
		/// Disconnects from the server, releases resources held by this
		/// object.
		/// </summary>
		public virtual void Dispose() {
#if !CORE14
			lock (m_lock) {
#endif
				if (IsDisposed)
					return;

				this.LogFunc("Dispose");
				this.LogStatus(FtpTraceLevel.Verbose, "Disposing FtpClient object...");

				try {
					if (IsConnected) {
						Disconnect();
					}
				} catch (Exception ex) {
					this.LogLine(FtpTraceLevel.Warn, "FtpClient.Dispose(): Caught and discarded an exception while disconnecting from host: " + ex.ToString());
				}

				if (m_stream != null) {
					try {
						m_stream.Dispose();
					} catch (Exception ex) {
						this.LogLine(FtpTraceLevel.Warn, "FtpClient.Dispose(): Caught and discarded an exception while disposing FtpStream object: " + ex.ToString());
					} finally {
						m_stream = null;
					}
				}

				m_credentials = null;
				m_textEncoding = null;
				m_host = null;
				m_asyncmethods.Clear();
				IsDisposed = true;
				GC.SuppressFinalize(this);
#if !CORE14
			}
#endif
		}

		/// <summary>
		/// Finalizer
		/// </summary>
		~FtpClient() {
			Dispose();
		}

		#endregion

		#region Clone

		/// <summary>
		/// Clones the control connection for opening multiple data streams
		/// </summary>
		/// <returns>A new control connection with the same property settings as this one</returns>
		/// <example><code source="..\Examples\CloneConnection.cs" lang="cs" /></example>
		protected FtpClient CloneConnection() {
			FtpClient conn = Create();

			conn.m_isClone = true;

			// configure new connection as clone of self
			conn.InternetProtocolVersions = InternetProtocolVersions;
			conn.SocketPollInterval = SocketPollInterval;
			conn.StaleDataCheck = StaleDataCheck;
			conn.EnableThreadSafeDataConnections = EnableThreadSafeDataConnections;
			conn.Encoding = Encoding;
			conn.Host = Host;
			conn.Port = Port;
			conn.Credentials = Credentials;
			conn.MaximumDereferenceCount = MaximumDereferenceCount;
			conn.ClientCertificates = ClientCertificates;
			conn.DataConnectionType = DataConnectionType;
			conn.UngracefullDisconnection = UngracefullDisconnection;
			conn.ConnectTimeout = ConnectTimeout;
			conn.ReadTimeout = ReadTimeout;
			conn.DataConnectionConnectTimeout = DataConnectionConnectTimeout;
			conn.DataConnectionReadTimeout = DataConnectionReadTimeout;
			conn.SocketKeepAlive = SocketKeepAlive;
			conn.Capabilities = Capabilities;
			conn.EncryptionMode = EncryptionMode;
			conn.DataConnectionEncryption = DataConnectionEncryption;
			conn.SslProtocols = SslProtocols;
			conn.SslBuffering = SslBuffering;
			conn.TransferChunkSize = TransferChunkSize;
			conn.ListingParser = ListingParser;
			conn.ListingCulture = ListingCulture;
			conn.TimeOffset = TimeOffset;
			conn.RetryAttempts = RetryAttempts;
			conn.UploadRateLimit = UploadRateLimit;
			conn.DownloadRateLimit = DownloadRateLimit;
			conn.DownloadDataType = DownloadDataType;
			conn.UploadDataType = UploadDataType;
			conn.ActivePorts = ActivePorts;

			// fix for #428: OpenRead with EnableThreadSafeDataConnections always uses ASCII
			conn.CurrentDataType = CurrentDataType;

#if !CORE
			conn.PlainTextEncryption = PlainTextEncryption;
#endif

			// copy props using attributes (slower, not .NET core compatible)
			/*foreach (PropertyInfo prop in GetType().GetProperties()) {
				object[] attributes = prop.GetCustomAttributes(typeof(FtpControlConnectionClone), true);

				if (attributes.Length > 0) {
					prop.SetValue(conn, prop.GetValue(this, null), null);
				}
			}*/

			// always accept certificate no matter what because if code execution ever
			// gets here it means the certificate on the control connection object being
			// cloned was already accepted.
			conn.ValidateCertificate += new FtpSslValidation(
				delegate (FtpClient obj, FtpSslValidationEventArgs e) {
					e.Accept = true;
				});

			return conn;
		}

		#endregion

		#region Execute Command

		/// <summary>
		/// Executes a command
		/// </summary>
		/// <param name="command">The command to execute</param>
		/// <returns>The servers reply to the command</returns>
		/// <example><code source="..\Examples\Execute.cs" lang="cs" /></example>
		public FtpReply Execute(string command) {
			FtpReply reply;

#if !CORE14
			lock (m_lock) {
#endif
				if (StaleDataCheck) {
					ReadStaleData(true, false, true);
				}

				if (!IsConnected) {
					if (command == "QUIT") {
						this.LogStatus(FtpTraceLevel.Info, "Not sending QUIT because the connection has already been closed.");
						return new FtpReply() {
							Code = "200",
							Message = "Connection already closed."
						};
					}

					Connect();
				}

				// hide sensitive data from logs
				string commandTxt = command;
				if (!FtpTrace.LogUserName && command.StartsWith("USER", StringComparison.Ordinal)) {
					commandTxt = "USER ***";
				}
				if (!FtpTrace.LogPassword && command.StartsWith("PASS", StringComparison.Ordinal)) {
					commandTxt = "PASS ***";
				}
				this.LogLine(FtpTraceLevel.Info, "Command:  " + commandTxt);

				// send command to FTP server
				m_stream.WriteLine(m_textEncoding, command);
				reply = GetReply();
#if !CORE14
			}
#endif

			return reply;
		}

#if !CORE
		delegate FtpReply AsyncExecute(string command);

		/// <summary>
		/// Performs execution of the specified command asynchronously
		/// </summary>
		/// <param name="command">The command to execute</param>
		/// <param name="callback">The <see cref="AsyncCallback"/> method</param>
		/// <param name="state">State object</param>
		/// <returns>IAsyncResult</returns>
		/// <example><code source="..\Examples\BeginExecute.cs" lang="cs" /></example>
		public IAsyncResult BeginExecute(string command, AsyncCallback callback, object state) {
			AsyncExecute func;
			IAsyncResult ar;

			lock (m_asyncmethods) {
				ar = (func = new AsyncExecute(Execute)).BeginInvoke(command, callback, state);
				m_asyncmethods.Add(ar, func);
			}

			return ar;
		}

		/// <summary>
		/// Ends an asynchronous command
		/// </summary>
		/// <param name="ar">IAsyncResult returned from BeginExecute</param>
		/// <returns>FtpReply object (never null).</returns>
		/// <example><code source="..\Examples\BeginExecute.cs" lang="cs" /></example>
		public FtpReply EndExecute(IAsyncResult ar) {
			return GetAsyncDelegate<AsyncExecute>(ar).EndInvoke(ar);
		}
#endif

#if ASYNC
		/// <summary>
		/// Performs an asynchronous execution of the specified command
		/// </summary>
		/// <param name="command">The command to execute</param>
		/// <param name="token">Cancellation Token</param>
		/// <returns>The servers reply to the command</returns>
		public async Task<FtpReply> ExecuteAsync(string command, CancellationToken token) {
            FtpReply reply;

            if (StaleDataCheck)
            {
#if CORE
                await ReadStaleDataAsync(true, false, true, token);
#else
                ReadStaleData(true, false, true);
#endif
            }

            if (!IsConnected)
            {
                if (command == "QUIT")
                {
                    this.LogStatus(FtpTraceLevel.Info, "Not sending QUIT because the connection has already been closed.");
                    return new FtpReply()
                    {
                        Code = "200",
                        Message = "Connection already closed."
                    };
                }

                await ConnectAsync(token);
            }

            // hide sensitive data from logs
            string commandTxt = command;
            if (!FtpTrace.LogUserName && command.StartsWith("USER", StringComparison.Ordinal))
            {
                commandTxt = "USER ***";
            }
            if (!FtpTrace.LogPassword && command.StartsWith("PASS", StringComparison.Ordinal))
            {
                commandTxt = "PASS ***";
            }
            this.LogLine(FtpTraceLevel.Info, "Command:  " + commandTxt);

            // send command to FTP server
            await m_stream.WriteLineAsync(m_textEncoding, command, token);
            reply = await GetReplyAsync(token);

            return reply;
        }
#endif

		#endregion

		#region Get Reply

		/// <summary>
		/// Retrieves a reply from the server. Do not execute this method
		/// unless you are sure that a reply has been sent, i.e., you
		/// executed a command. Doing so will cause the code to hang
		/// indefinitely waiting for a server reply that is never coming.
		/// </summary>
		/// <returns>FtpReply representing the response from the server</returns>
		/// <example><code source="..\Examples\BeginGetReply.cs" lang="cs" /></example>
		public FtpReply GetReply() {
			FtpReply reply = new FtpReply();
			string buf;

#if !CORE14
			lock (m_lock) {
#endif
				if (!IsConnected)
					throw new InvalidOperationException("No connection to the server has been established.");

				m_stream.ReadTimeout = m_readTimeout;
				while ((buf = m_stream.ReadLine(Encoding)) != null) {
					Match m;


					if ((m = Regex.Match(buf, "^(?<code>[0-9]{3}) (?<message>.*)$")).Success) {
						reply.Code = m.Groups["code"].Value;
						reply.Message = m.Groups["message"].Value;
						break;
					}

					reply.InfoMessages += (buf + "\n");
				}

				// if reply received
				if (reply.Code != null) {

					// hide sensitive data from logs
					string logMsg = reply.Message;
					if (!FtpTrace.LogUserName && reply.Code == "331" && logMsg.StartsWith("User ", StringComparison.Ordinal) && logMsg.Contains(" OK")) {
						logMsg = logMsg.Replace(Credentials.UserName, "***");
					}

					// log response code + message
					this.LogLine(FtpTraceLevel.Info, "Response: " + reply.Code + " " + logMsg);
				}

				// log multiline response messages
				if (reply.InfoMessages != null) {
					reply.InfoMessages = reply.InfoMessages.Trim();
				}
				if (!string.IsNullOrEmpty(reply.InfoMessages)) {
					//this.LogLine(FtpTraceLevel.Verbose, "+---------------------------------------+");
					this.LogLine(FtpTraceLevel.Verbose, reply.InfoMessages.Split('\n').AddPrefix("Response: ", true).Join("\n"));
					//this.LogLine(FtpTraceLevel.Verbose, "-----------------------------------------");
				}
#if !CORE14
			}
#endif
			LastReply = reply;

			return reply;
		}

#if ASYNC
        // TODO: add example
        /// <summary>
        /// Retrieves a reply from the server. Do not execute this method
        /// unless you are sure that a reply has been sent, i.e., you
        /// executed a command. Doing so will cause the code to hang
        /// indefinitely waiting for a server reply that is never coming.
        /// </summary>
        /// <returns>FtpReply representing the response from the server</returns>
        /// <example><code source="..\Examples\BeginGetReply.cs" lang="cs" /></example>
        public async Task<FtpReply> GetReplyAsync(CancellationToken token)
        {
            FtpReply reply = new FtpReply();
            string buf;

            if (!IsConnected)
                throw new InvalidOperationException("No connection to the server has been established.");

            m_stream.ReadTimeout = m_readTimeout;
            while ((buf = await m_stream.ReadLineAsync(Encoding, token)) != null)
            {
                Match m;


                if ((m = Regex.Match(buf, "^(?<code>[0-9]{3}) (?<message>.*)$")).Success)
                {
                    reply.Code = m.Groups["code"].Value;
                    reply.Message = m.Groups["message"].Value;
                    break;
                }

                reply.InfoMessages += (buf + "\n");
            }

            // if reply received
            if (reply.Code != null)
            {

                // hide sensitive data from logs
                string logMsg = reply.Message;
                if (!FtpTrace.LogUserName && reply.Code == "331" && logMsg.StartsWith("User ", StringComparison.Ordinal) && logMsg.Contains(" OK"))
                {
                    logMsg = logMsg.Replace(Credentials.UserName, "***");
                }

                // log response code + message
                this.LogLine(FtpTraceLevel.Info, "Response: " + reply.Code + " " + logMsg);
            }

            // log multiline response messages
            if (reply.InfoMessages != null)
            {
                reply.InfoMessages = reply.InfoMessages.Trim();
            }
            if (!string.IsNullOrEmpty(reply.InfoMessages))
            {
                //this.LogLine(FtpTraceLevel.Verbose, "+---------------------------------------+");
                this.LogLine(FtpTraceLevel.Verbose, reply.InfoMessages.Split('\n').AddPrefix("Response: ", true).Join("\n"));
                //this.LogLine(FtpTraceLevel.Verbose, "-----------------------------------------");
            }

            return reply;
        }
#endif

		#endregion

		#region Connect

		private FtpListParser m_listParser;

		/// <summary>
		/// Connect to the server
		/// </summary>
		/// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
		/// <example><code source="..\Examples\Connect.cs" lang="cs" /></example>
		public virtual void Connect() {
			FtpReply reply;

#if !CORE14
			lock (m_lock) {
#endif

				this.LogFunc("Connect");

				if (IsDisposed)
					throw new ObjectDisposedException("This FtpClient object has been disposed. It is no longer accessible.");

				if (m_stream == null) {
					m_stream = new FtpSocketStream(m_SslProtocols);
					m_stream.Client = this;
					m_stream.ValidateCertificate += new FtpSocketStreamSslValidation(FireValidateCertficate);
				} else {
					if (IsConnected) {
						Disconnect();
					}
				}

				if (Host == null) {
					throw new FtpException("No host has been specified");
				}

				if (!IsClone) {
					m_caps = FtpCapability.NONE;
				}

				m_hashAlgorithms = FtpHashAlgorithm.NONE;
				m_stream.ConnectTimeout = m_connectTimeout;
				m_stream.SocketPollInterval = m_socketPollInterval;
				Connect(m_stream);

				m_stream.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, m_keepAlive);

#if !NO_SSL
				if (EncryptionMode == FtpEncryptionMode.Implicit) {
					m_stream.ActivateEncryption(Host, m_clientCerts.Count > 0 ? m_clientCerts : null, m_SslProtocols);
				}
#endif

				Handshake();
				DetectFtpServer();

#if !NO_SSL
				if (EncryptionMode == FtpEncryptionMode.Explicit) {
					if (!(reply = Execute("AUTH TLS")).Success) {
						throw new FtpSecurityNotAvailableException("AUTH TLS command failed.");
					}
					m_stream.ActivateEncryption(Host, m_clientCerts.Count > 0 ? m_clientCerts : null, m_SslProtocols);
				}
#endif

				if (m_credentials != null) {
					Authenticate();
				}

				if (m_stream.IsEncrypted && DataConnectionEncryption) {
					if (!(reply = Execute("PBSZ 0")).Success) {
						throw new FtpCommandException(reply);
					}
					if (!(reply = Execute("PROT P")).Success) {
						throw new FtpCommandException(reply);
					}
				}

				// if this is a clone these values should have already been loaded
				// so save some bandwidth and CPU time and skip executing this again.
				if (!IsClone && m_checkCapabilities) {
					if ((reply = Execute("FEAT")).Success && reply.InfoMessages != null) {
						GetFeatures(reply);
					} else {
						AssumeCapabilities();
					}
				}

				// Enable UTF8 if the encoding is ASCII and UTF8 is supported
				if (m_textEncodingAutoUTF && m_textEncoding == Encoding.ASCII && HasFeature(FtpCapability.UTF8)) {
					m_textEncoding = Encoding.UTF8;
				}

				this.LogStatus(FtpTraceLevel.Info, "Text encoding: " + m_textEncoding.ToString());

				if (m_textEncoding == Encoding.UTF8) {
					// If the server supports UTF8 it should already be enabled and this
					// command should not matter however there are conflicting drafts
					// about this so we'll just execute it to be safe. 
					Execute("OPTS UTF8 ON");
				}

				// Get the system type - Needed to auto-detect file listing parser
				if ((reply = Execute("SYST")).Success) {
					m_systemType = reply.Message;
					DetectFtpServerBySyst();
				}

#if !NO_SSL && !CORE
				if (m_stream.IsEncrypted && PlainTextEncryption) {
					if (!(reply = Execute("CCC")).Success) {
						throw new FtpSecurityNotAvailableException("Failed to disable encryption with CCC command. Perhaps your server does not support it or is not configured to allow it.");
					} else {

						// close the SslStream and send close_notify command to server
						m_stream.DeactivateEncryption();

						// read stale data (server's reply?)
						ReadStaleData(false, true, false);
					}
				}
#endif

				// Create the parser even if the auto-OS detection failed
				m_listParser.Init(m_systemType);

				// FIX : #318 always set the type when we create a new connection
				ForceSetDataType = true;

#if !CORE14
			}
#endif
		}

#if ASYNC
        // TODO: add example
        /// <summary>
        /// Connect to the server
        /// </summary>
        /// <exception cref="ObjectDisposedException">Thrown if this object has been disposed.</exception>
        /// <example><code source="..\Examples\Connect.cs" lang="cs" /></example>
        public virtual async Task ConnectAsync(CancellationToken token = default(CancellationToken))
        {
            FtpReply reply;

            this.LogFunc(nameof(ConnectAsync));

            if (IsDisposed)
                throw new ObjectDisposedException("This FtpClient object has been disposed. It is no longer accessible.");

            if (m_stream == null)
            {
                m_stream = new FtpSocketStream(m_SslProtocols);
				m_stream.Client = this;
                m_stream.ValidateCertificate += new FtpSocketStreamSslValidation(FireValidateCertficate);
            }
            else
            {
                if (IsConnected)
                {
                    Disconnect();
                }
            }

            if (Host == null)
            {
                throw new FtpException("No host has been specified");
            }

            if (!IsClone)
            {
                m_caps = FtpCapability.NONE;
            }

            m_hashAlgorithms = FtpHashAlgorithm.NONE;
            m_stream.ConnectTimeout = m_connectTimeout;
            m_stream.SocketPollInterval = m_socketPollInterval;
            await ConnectAsync(m_stream, token);

            m_stream.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.KeepAlive, m_keepAlive);

#if !NO_SSL
            if (EncryptionMode == FtpEncryptionMode.Implicit) {
                await m_stream.ActivateEncryptionAsync(Host, m_clientCerts.Count > 0 ? m_clientCerts : null, m_SslProtocols);
            }
#endif

            await HandshakeAsync(token);
			DetectFtpServer();

#if !NO_SSL
            if (EncryptionMode == FtpEncryptionMode.Explicit) {
                if (!(reply = await ExecuteAsync("AUTH TLS", token)).Success) {
                    throw new FtpSecurityNotAvailableException("AUTH TLS command failed.");
                }
                await m_stream.ActivateEncryptionAsync(Host, m_clientCerts.Count > 0 ? m_clientCerts : null, m_SslProtocols);
            }
#endif

			if (m_credentials != null)
            {
                await AuthenticateAsync(token);
            }

            if (m_stream.IsEncrypted && DataConnectionEncryption)
            {
                if (!(reply = await ExecuteAsync("PBSZ 0", token)).Success){
                    throw new FtpCommandException(reply);
				}
                if (!(reply = await ExecuteAsync("PROT P", token)).Success){
                    throw new FtpCommandException(reply);
				}
            }

			// if this is a clone these values should have already been loaded
			// so save some bandwidth and CPU time and skip executing this again.
			if (!IsClone && m_checkCapabilities) {
				if ((reply = await ExecuteAsync("FEAT", token)).Success && reply.InfoMessages != null)
                {
                    GetFeatures(reply);
                }else {
						AssumeCapabilities();
				}
            }

            // Enable UTF8 if the encoding is ASCII and UTF8 is supported
            if (m_textEncodingAutoUTF && m_textEncoding == Encoding.ASCII && HasFeature(FtpCapability.UTF8))
            {
                m_textEncoding = Encoding.UTF8;
            }

            this.LogStatus(FtpTraceLevel.Info, "Text encoding: " + m_textEncoding.ToString());

            if (m_textEncoding == Encoding.UTF8)
            {
                // If the server supports UTF8 it should already be enabled and this
                // command should not matter however there are conflicting drafts
                // about this so we'll just execute it to be safe. 
                await ExecuteAsync("OPTS UTF8 ON", token);
            }

            // Get the system type - Needed to auto-detect file listing parser
            if ((reply = await ExecuteAsync("SYST", token)).Success)
            {
                m_systemType = reply.Message;
				DetectFtpServerBySyst();
            }

#if !NO_SSL && !CORE
            if (m_stream.IsEncrypted && PlainTextEncryption) {
                if (!(reply = await ExecuteAsync("CCC", token)).Success)
                {
                    throw new FtpSecurityNotAvailableException("Failed to disable encryption with CCC command. Perhaps your server does not support it or is not configured to allow it.");
                } else {

                    // close the SslStream and send close_notify command to server
                    m_stream.DeactivateEncryption();

                    // read stale data (server's reply?)
                    await ReadStaleDataAsync(false, true, false, token);
                }
            }
#endif

			// Create the parser even if the auto-OS detection failed
			m_listParser.Init(m_systemType);
        }
#endif

		/// <summary>
		/// Connect to the FTP server. Overwritten in proxy classes.
		/// </summary>
		/// <param name="stream"></param>
		protected virtual void Connect(FtpSocketStream stream) {
			stream.Connect(Host, Port, InternetProtocolVersions);
		}

#if ASYNC
		/// <summary>
		/// Connect to the FTP server. Overwritten in proxy classes.
		/// </summary>
		/// <param name="stream"></param>
		/// <param name="token"></param>
		protected virtual async Task ConnectAsync(FtpSocketStream stream, CancellationToken token)
        {
            await stream.ConnectAsync(Host, Port, InternetProtocolVersions, token);
        }
#endif

		/// <summary>
		/// Connect to the FTP server. Overwritten in proxy classes.
		/// </summary>
		protected virtual void Connect(FtpSocketStream stream, string host, int port, FtpIpVersion ipVersions) {
			stream.Connect(host, port, ipVersions);
		}

#if ASYNC
        /// <summary>
        /// Connect to the FTP server. Overwritten in proxy classes.
        /// </summary>
        protected virtual Task ConnectAsync(FtpSocketStream stream, string host, int port, FtpIpVersion ipVersions, CancellationToken token)
        {
            return stream.ConnectAsync(host, port, ipVersions, token);
        }
#endif

		protected FtpReply HandshakeReply;

		/// <summary>
		/// Called during Connect(). Typically extended by FTP proxies.
		/// </summary>
		protected virtual void Handshake() {
			FtpReply reply;
			if (!(reply = GetReply()).Success) {
				if (reply.Code == null) {
					throw new IOException("The connection was terminated before a greeting could be read.");
				} else {
					throw new FtpCommandException(reply);
				}
			}
			HandshakeReply = reply;
		}

#if ASYNC
        /// <summary>
        /// Called during <see cref="ConnectAsync()"/>. Typically extended by FTP proxies.
        /// </summary>
        protected virtual async Task HandshakeAsync(CancellationToken token = default(CancellationToken))
        {
            FtpReply reply;
            if (!(reply = await GetReplyAsync(token)).Success)
            {
                if (reply.Code == null)
                {
                    throw new IOException("The connection was terminated before a greeting could be read.");
                }
                else
                {
                    throw new FtpCommandException(reply);
                }
            }
			HandshakeReply = reply;
        }
#endif

		/// <summary>
		/// Populates the capabilities flags based on capabilities
		/// supported by this server. This method is overridable
		/// so that new features can be supported
		/// </summary>
		/// <param name="reply">The reply object from the FEAT command. The InfoMessages property will
		/// contain a list of the features the server supported delimited by a new line '\n' character.</param>
		protected virtual void GetFeatures(FtpReply reply) {
			GetFeatures(reply.InfoMessages.Split('\n'));
		}

#if !CORE
		delegate void AsyncConnect();

		/// <summary>
		/// Initiates a connection to the server
		/// </summary>
		/// <param name="callback">AsyncCallback method</param>
		/// <param name="state">State object</param>
		/// <returns>IAsyncResult</returns>
		/// <example><code source="..\Examples\BeginConnect.cs" lang="cs" /></example>
		public IAsyncResult BeginConnect(AsyncCallback callback, object state) {
			AsyncConnect func;
			IAsyncResult ar;

			lock (m_asyncmethods) {
				ar = (func = new AsyncConnect(Connect)).BeginInvoke(callback, state);
				m_asyncmethods.Add(ar, func);
			}

			return ar;
		}

		/// <summary>
		/// Ends an asynchronous connection attempt to the server from <see cref="BeginConnect"/>
		/// </summary>
		/// <param name="ar"><see cref="IAsyncResult"/> returned from <see cref="BeginConnect"/></param>
		/// <example><code source="..\Examples\BeginConnect.cs" lang="cs" /></example>
		public void EndConnect(IAsyncResult ar) {
			GetAsyncDelegate<AsyncConnect>(ar).EndInvoke(ar);
		}
#endif

		#endregion

		#region Login

		/// <summary>
		/// Performs a login on the server. This method is overridable so
		/// that the login procedure can be changed to support, for example,
		/// a FTP proxy.
		/// </summary>
		protected virtual void Authenticate() {
			Authenticate(Credentials.UserName, Credentials.Password);
		}

#if ASYNC
        /// <summary>
        /// Performs a login on the server. This method is overridable so
        /// that the login procedure can be changed to support, for example,
        /// a FTP proxy.
        /// </summary>
        protected virtual async Task AuthenticateAsync(CancellationToken token)
        {
            await AuthenticateAsync(Credentials.UserName, Credentials.Password, token);
        }
#endif

		/// <summary>
		/// Performs a login on the server. This method is overridable so
		/// that the login procedure can be changed to support, for example,
		/// a FTP proxy.
		/// </summary>
		/// <exception cref="FtpAuthenticationException">On authentication failures</exception>
		/// <remarks>
		/// To handle authentication failures without retries, catch FtpAuthenticationException.
		/// </remarks>
		protected virtual void Authenticate(string userName, string password) {
			FtpReply reply;

			if (!(reply = Execute("USER " + userName)).Success) {
				throw new FtpAuthenticationException(reply);
			}

			if (reply.Type == FtpResponseType.PositiveIntermediate &&
				!(reply = Execute("PASS " + password)).Success) {
				throw new FtpAuthenticationException(reply);
			}
		}

#if ASYNC
        /// <summary>
        /// Performs a login on the server. This method is overridable so
        /// that the login procedure can be changed to support, for example,
        /// a FTP proxy.
        /// </summary>
		/// <exception cref="FtpAuthenticationException">On authentication failures</exception>
		/// <remarks>
		/// To handle authentication failures without retries, catch FtpAuthenticationException.
		/// </remarks>
        protected virtual async Task AuthenticateAsync(string userName, string password, CancellationToken token)
        {
            FtpReply reply;

            if (!(reply = await ExecuteAsync("USER " + userName, token)).Success){
                throw new FtpAuthenticationException(reply);
			}

            if (reply.Type == FtpResponseType.PositiveIntermediate
                && !(reply = await ExecuteAsync("PASS " + password, token)).Success){
                throw new FtpAuthenticationException(reply);
			}
        }
#endif

		#endregion

		#region Disconnect

		/// <summary>
		/// Disconnects from the server
		/// </summary>
		public virtual void Disconnect() {
#if !CORE14
			lock (m_lock) {
#endif
				if (m_stream != null && m_stream.IsConnected) {
					try {
						if (!UngracefullDisconnection) {
							Execute("QUIT");
						}
					} catch (SocketException sockex) {
						this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): SocketException caught and discarded while closing control connection: " + sockex.ToString());
					} catch (IOException ioex) {
						this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): IOException caught and discarded while closing control connection: " + ioex.ToString());
					} catch (FtpCommandException cmdex) {
						this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): FtpCommandException caught and discarded while closing control connection: " + cmdex.ToString());
					} catch (FtpException ftpex) {
						this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): FtpException caught and discarded while closing control connection: " + ftpex.ToString());
					} finally {
						m_stream.Close();
					}
				}
#if !CORE14
			}
#endif
		}

#if !CORE
		delegate void AsyncDisconnect();

		/// <summary>
		/// Initiates a disconnection on the server
		/// </summary>
		/// <param name="callback"><see cref="AsyncCallback"/> method</param>
		/// <param name="state">State object</param>
		/// <returns>IAsyncResult</returns>
		/// <example><code source="..\Examples\BeginDisconnect.cs" lang="cs" /></example>
		public IAsyncResult BeginDisconnect(AsyncCallback callback, object state) {
			IAsyncResult ar;
			AsyncDisconnect func;

			lock (m_asyncmethods) {
				ar = (func = new AsyncDisconnect(Disconnect)).BeginInvoke(callback, state);
				m_asyncmethods.Add(ar, func);
			}

			return ar;
		}

		/// <summary>
		/// Ends a call to <see cref="BeginDisconnect"/>
		/// </summary>
		/// <param name="ar"><see cref="IAsyncResult"/> returned from <see cref="BeginDisconnect"/></param>
		/// <example><code source="..\Examples\BeginConnect.cs" lang="cs" /></example>
		public void EndDisconnect(IAsyncResult ar) {
			GetAsyncDelegate<AsyncDisconnect>(ar).EndInvoke(ar);
		}

#endif
#if ASYNC
		/// <summary>
		/// Disconnects from the server asynchronously
		/// </summary>
		public async Task DisconnectAsync(CancellationToken token = default(CancellationToken)) {
			if (m_stream != null && m_stream.IsConnected)
			{
				try
				{
					if (!UngracefullDisconnection)
					{
						await ExecuteAsync("QUIT", token);
					}
				}
				catch (SocketException sockex)
				{
					this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): SocketException caught and discarded while closing control connection: " + sockex.ToString());
				}
				catch (IOException ioex)
				{
					this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): IOException caught and discarded while closing control connection: " + ioex.ToString());
				}
				catch (FtpCommandException cmdex)
				{
					this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): FtpCommandException caught and discarded while closing control connection: " + cmdex.ToString());
				}
				catch (FtpException ftpex)
				{
					this.LogStatus(FtpTraceLevel.Warn, "FtpClient.Disconnect(): FtpException caught and discarded while closing control connection: " + ftpex.ToString());
				}
				finally
				{
					m_stream.Close();
				}
			}
		}
#endif

		#endregion

		#region FTPS

		/// <summary>
		/// Catches the socket stream ssl validation event and fires the event handlers
		/// attached to this object for validating SSL certificates
		/// </summary>
		/// <param name="stream">The stream that fired the event</param>
		/// <param name="e">The event args used to validate the certificate</param>
		void FireValidateCertficate(FtpSocketStream stream, FtpSslValidationEventArgs e) {
			OnValidateCertficate(e);
		}

		/// <summary>
		/// Fires the SSL validation event
		/// </summary>
		/// <param name="e">Event Args</param>
		void OnValidateCertficate(FtpSslValidationEventArgs e) {
			FtpSslValidation evt;

			evt = m_sslvalidate;
			if (evt != null)
				evt(this, e);
		}

		#endregion

		#region Utils

		/// <summary>
		/// Performs a bitwise and to check if the specified
		/// flag is set on the <see cref="Capabilities"/>  property.
		/// </summary>
		/// <param name="cap">The <see cref="FtpCapability"/> to check for</param>
		/// <returns>True if the feature was found, false otherwise</returns>
		public bool HasFeature(FtpCapability cap) {
			return ((this.Capabilities & cap) == cap);
		}

		/// <summary>
		/// Retrieves the delegate for the specified IAsyncResult and removes
		/// it from the m_asyncmethods collection if the operation is successful
		/// </summary>
		/// <typeparam name="T">Type of delegate to retrieve</typeparam>
		/// <param name="ar">The IAsyncResult to retrieve the delegate for</param>
		/// <returns>The delegate that generated the specified IAsyncResult</returns>
		protected T GetAsyncDelegate<T>(IAsyncResult ar) {
			T func;

			lock (m_asyncmethods) {
				if (m_isDisposed) {
					throw new ObjectDisposedException("This connection object has already been disposed.");
				}

				if (!m_asyncmethods.ContainsKey(ar))
					throw new InvalidOperationException("The specified IAsyncResult could not be located.");

				if (!(m_asyncmethods[ar] is T)) {
#if CORE
					throw new InvalidCastException("The AsyncResult cannot be matched to the specified delegate. ");
#else
					StackTrace st = new StackTrace(1);

					throw new InvalidCastException("The AsyncResult cannot be matched to the specified delegate. " +
						("Are you sure you meant to call " + st.GetFrame(0).GetMethod().Name + " and not another method?")
					);
#endif
				}

				func = (T)m_asyncmethods[ar];
				m_asyncmethods.Remove(ar);
			}

			return func;
		}

		/// <summary>
		/// Ensure a relative path is absolute by appending the working dir
		/// </summary>
		private string GetAbsolutePath(string path) {
			if (path == null || path.Trim().Length == 0) {

				// if path not given, then use working dir
				string pwd = GetWorkingDirectory();
				if (pwd != null && pwd.Trim().Length > 0)
					path = pwd;
				else
					path = "./";

				// FIX : #153 ensure this check works with unix & windows
			} else if (!path.StartsWith("/") && path.Substring(1, 1) != ":") {
				
				// if its a server-specific absolute path then don't add base dir
				if (IsAbsolutePath(path)) {
					return path;
				}

				// if relative path given then add working dir to calc full path
				string pwd = GetWorkingDirectory();
				if (pwd != null && pwd.Trim().Length > 0) {
					if (path.StartsWith("./"))
						path = path.Remove(0, 2);
					path = (pwd + "/" + path).GetFtpPath();
				}
			}
			return path;
		}

#if ASYNC
        /// <summary>
        /// Ensure a relative path is absolute by appending the working dir
        /// </summary>
        private async Task<string> GetAbsolutePathAsync(string path, CancellationToken token)
        {
            if (path == null || path.Trim().Length == 0)
            {

                // if path not given, then use working dir
                string pwd = await GetWorkingDirectoryAsync(token);
                if (pwd != null && pwd.Trim().Length > 0)
                    path = pwd;
                else
                    path = "./";

            }
            else if (!path.StartsWith("/"))
            {

                // if relative path given then add working dir to calc full path
                string pwd = await GetWorkingDirectoryAsync(token);
                if (pwd != null && pwd.Trim().Length > 0)
                {
                    if (path.StartsWith("./"))
                        path = path.Remove(0, 2);
                    path = (pwd + "/" + path).GetFtpPath();
                }
            }
            return path;
        }
#endif

		private static string DecodeUrl(string url) {
#if CORE
			return WebUtility.UrlDecode(url);
#else
			return HttpUtility.UrlDecode(url);
#endif
		}

		private static byte[] ReadToEnd(Stream stream, long maxLength, int chunkLen) {
			int read = 1;
			byte[] buffer = new byte[chunkLen];
			using (var mem = new MemoryStream()) {
				do {
					long length = maxLength == 0 ? buffer.Length : Math.Min(maxLength - (int)mem.Length, buffer.Length);
					read = stream.Read(buffer, 0, (int)length);
					mem.Write(buffer, 0, read);
					if (maxLength > 0 && mem.Length == maxLength) break;
				} while (read > 0);

				return mem.ToArray();
			}
		}

		/// <summary>
		/// Disables UTF8 support and changes the Encoding property
		/// back to ASCII. If the server returns an error when trying
		/// to turn UTF8 off a FtpCommandException will be thrown.
		/// </summary>
		public void DisableUTF8() {
			FtpReply reply;

#if !CORE14
			lock (m_lock) {
#endif
				if (!(reply = Execute("OPTS UTF8 OFF")).Success) {
					throw new FtpCommandException(reply);
				}

				m_textEncoding = Encoding.ASCII;
				m_textEncodingAutoUTF = false;
#if !CORE14
			}
#endif
		}

		/// <summary>
		/// Data shouldn't be on the socket, if it is it probably
		/// means we've been disconnected. Read and discard
		/// whatever is there and close the connection (optional).
		/// </summary>
		/// <param name="closeStream">close the connection?</param>
		/// <param name="evenEncrypted">even read encrypted data?</param>
		/// <param name="traceData">trace data to logs?</param>
		private void ReadStaleData(bool closeStream, bool evenEncrypted, bool traceData) {
			if (m_stream != null && m_stream.SocketDataAvailable > 0) {
				if (traceData) {
					this.LogStatus(FtpTraceLevel.Info, "There is stale data on the socket, maybe our connection timed out or you did not call GetReply(). Re-connecting...");
				}
				if (m_stream.IsConnected && (!m_stream.IsEncrypted || evenEncrypted)) {
					byte[] buf = new byte[m_stream.SocketDataAvailable];
					m_stream.RawSocketRead(buf);
					if (traceData) {
						this.LogStatus(FtpTraceLevel.Verbose, "The stale data was: " + Encoding.GetString(buf).TrimEnd('\r', '\n'));
					}
				}

				if (closeStream) {
					m_stream.Close();
				}
			}
		}

#if ASYNC
		/// <summary>
		/// Data shouldn't be on the socket, if it is it probably
		/// means we've been disconnected. Read and discard
		/// whatever is there and close the connection (optional).
		/// </summary>
		/// <param name="closeStream">close the connection?</param>
		/// <param name="evenEncrypted">even read encrypted data?</param>
		/// <param name="traceData">trace data to logs?</param>
		/// <param name="token">Cancellation Token</param>

		private async Task ReadStaleDataAsync(bool closeStream, bool evenEncrypted, bool traceData, CancellationToken token)
        {
            if (m_stream != null && m_stream.SocketDataAvailable > 0)
            {
                if (traceData)
                {
                    this.LogStatus(FtpTraceLevel.Info, "There is stale data on the socket, maybe our connection timed out or you did not call GetReply(). Re-connecting...");
                }
                if (m_stream.IsConnected && (!m_stream.IsEncrypted || evenEncrypted))
                {
                    byte[] buf = new byte[m_stream.SocketDataAvailable];
                    await m_stream.RawSocketReadAsync(buf, token);
                    if (traceData)
                    {
                        this.LogStatus(FtpTraceLevel.Verbose, "The stale data was: " + Encoding.GetString(buf).TrimEnd('\r', '\n'));
                    }
                }

                if (closeStream)
                {
                    m_stream.Close();
                }
            }
        }
#endif

		/// <summary>
		/// Checks if this FTP/FTPS connection is made through a proxy.
		/// </summary>
		public bool IsProxy() {
			return (this is FtpClientProxy);
		}

		private static string[] fileNotFoundStrings = new string[] { "can't find file", "can't check for file existence", "does not exist", "failed to open file", "not found", "no such file", "cannot find the file", "cannot find", "could not get file", "not a regular file", "file unavailable", "file is unavailable", "file not unavailable", "file is not available", "no files found", "no file found" };
		

		#endregion

		#region Logging

		/// <summary>
		/// Add a custom listener here to get events every time a message is logged.
		/// </summary>
		public Action<FtpTraceLevel, string> OnLogEvent;

		/// <summary>
		/// Log a function call with relavent arguments
		/// </summary>
		/// <param name="function">The name of the API function</param>
		/// <param name="args">The args passed to the function</param>
		public void LogFunc(string function, object[] args = null) {

			// log to attached logger if given
			if (OnLogEvent != null) {
				OnLogEvent(FtpTraceLevel.Verbose, ">         " + function + "(" + args.ItemsToString().Join(", ") + ")");
			}

			// log to system
			FtpTrace.WriteFunc(function, args);
		}
		/// <summary>
		/// Log a message
		/// </summary>
		/// <param name="eventType">The type of tracing event</param>
		/// <param name="message">The message to write</param>
		public void LogLine(FtpTraceLevel eventType, string message) {

			// log to attached logger if given
			if (OnLogEvent != null) {
				OnLogEvent(eventType, message);
			}

			// log to system
			FtpTrace.WriteLine(eventType, message);
		}

		/// <summary>
		/// Log a message, adding an automatic prefix to the message based on the `eventType`
		/// </summary>
		/// <param name="eventType">The type of tracing event</param>
		/// <param name="message">The message to write</param>
		public void LogStatus(FtpTraceLevel eventType, string message) {

			// add prefix
			message = TraceLevelPrefix(eventType) + message;

			// log to attached logger if given
			if (OnLogEvent != null) {
				OnLogEvent(eventType, message);
			}

			// log to system
			FtpTrace.WriteLine(eventType, message);
		}
		private static string TraceLevelPrefix(FtpTraceLevel level) {
			switch (level) {
				case FtpTraceLevel.Verbose:
					return "Status:   ";
				case FtpTraceLevel.Info:
					return "Status:   ";
				case FtpTraceLevel.Warn:
					return "Warning:  ";
				case FtpTraceLevel.Error:
					return "Error:    ";
			}
			return "Status:   ";
		}
		#endregion

		#region Static API

		/// <summary>
		/// Calculate the CHMOD integer value given a set of permissions.
		/// </summary>
		public static int CalcChmod(FtpPermission owner, FtpPermission group, FtpPermission other) {

			int chmod = 0;

			if (HasPermission(owner, FtpPermission.Read)) {
				chmod += 400;
			}
			if (HasPermission(owner, FtpPermission.Write)) {
				chmod += 200;
			}
			if (HasPermission(owner, FtpPermission.Execute)) {
				chmod += 100;
			}

			if (HasPermission(group, FtpPermission.Read)) {
				chmod += 40;
			}
			if (HasPermission(group, FtpPermission.Write)) {
				chmod += 20;
			}
			if (HasPermission(group, FtpPermission.Execute)) {
				chmod += 10;
			}

			if (HasPermission(other, FtpPermission.Read)) {
				chmod += 4;
			}
			if (HasPermission(other, FtpPermission.Write)) {
				chmod += 2;
			}
			if (HasPermission(other, FtpPermission.Execute)) {
				chmod += 1;
			}

			return chmod;
		}

		private static bool HasPermission(FtpPermission owner, FtpPermission flag) {
			return (owner & flag) == flag;
		}

		//TODO:  Create async versions of static methods

		/// <summary>
		/// Connects to the specified URI. If the path specified by the URI ends with a
		/// / then the working directory is changed to the path specified.
		/// </summary>
		/// <param name="uri">The URI to parse</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <returns>FtpClient object</returns>
		public static FtpClient Connect(Uri uri, bool checkcertificate) {
			FtpClient cl = new FtpClient();

			if (uri == null)
				throw new ArgumentException("Invalid URI object");

			switch (uri.Scheme.ToLower()) {
				case "ftp":
				case "ftps":
					break;
				default:
					throw new UriFormatException("The specified URI scheme is not supported. Please use ftp:// or ftps://");
			}

			cl.Host = uri.Host;
			cl.Port = uri.Port;

			if (uri.UserInfo != null && uri.UserInfo.Length > 0) {
				if (uri.UserInfo.Contains(":")) {
					string[] parts = uri.UserInfo.Split(':');

					if (parts.Length != 2)
						throw new UriFormatException("The user info portion of the URI contains more than 1 colon. The username and password portion of the URI should be URL encoded.");

					cl.Credentials = new NetworkCredential(DecodeUrl(parts[0]), DecodeUrl(parts[1]));
				} else
					cl.Credentials = new NetworkCredential(DecodeUrl(uri.UserInfo), "");
			} else {
				// if no credentials were supplied just make up
				// some for anonymous authentication.
				cl.Credentials = new NetworkCredential("ftp", "ftp");
			}

			cl.ValidateCertificate += new FtpSslValidation(delegate (FtpClient control, FtpSslValidationEventArgs e) {
				if (e.PolicyErrors != System.Net.Security.SslPolicyErrors.None && checkcertificate)
					e.Accept = false;
				else
					e.Accept = true;
			});

			cl.Connect();

			if (uri.PathAndQuery != null && uri.PathAndQuery.EndsWith("/"))
				cl.SetWorkingDirectory(uri.PathAndQuery);

			return cl;
		}

		/// <summary>
		/// Connects to the specified URI. If the path specified by the URI ends with a
		/// / then the working directory is changed to the path specified.
		/// </summary>
		/// <param name="uri">The URI to parse</param>
		/// <returns>FtpClient object</returns>
		public static FtpClient Connect(Uri uri) {
			return Connect(uri, true);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <param name="datatype">ASCII/Binary mode</param>
		/// <param name="restart">Restart location</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenReadURI.cs" lang="cs" /></example>
		public static Stream OpenRead(Uri uri, bool checkcertificate, FtpDataType datatype, long restart) {
			FtpClient cl = null;

			CheckURI(uri);

			cl = Connect(uri, checkcertificate);
			cl.EnableThreadSafeDataConnections = false;

			return cl.OpenRead(uri.PathAndQuery, datatype, restart);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <param name="datatype">ASCII/Binary mode</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenReadURI.cs" lang="cs" /></example>
		public static Stream OpenRead(Uri uri, bool checkcertificate, FtpDataType datatype) {
			return OpenRead(uri, checkcertificate, datatype, 0);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenReadURI.cs" lang="cs" /></example>
		public static Stream OpenRead(Uri uri, bool checkcertificate) {
			return OpenRead(uri, checkcertificate, FtpDataType.Binary, 0);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenReadURI.cs" lang="cs" /></example>
		public static Stream OpenRead(Uri uri) {
			return OpenRead(uri, true, FtpDataType.Binary, 0);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <param name="datatype">ASCII/Binary mode</param> 
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenWriteURI.cs" lang="cs" /></example>
		public static Stream OpenWrite(Uri uri, bool checkcertificate, FtpDataType datatype) {
			FtpClient cl = null;

			CheckURI(uri);

			cl = Connect(uri, checkcertificate);
			cl.EnableThreadSafeDataConnections = false;

			return cl.OpenWrite(uri.PathAndQuery, datatype);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenWriteURI.cs" lang="cs" /></example>
		public static Stream OpenWrite(Uri uri, bool checkcertificate) {
			return OpenWrite(uri, checkcertificate, FtpDataType.Binary);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenWriteURI.cs" lang="cs" /></example>
		public static Stream OpenWrite(Uri uri) {
			return OpenWrite(uri, true, FtpDataType.Binary);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <param name="datatype">ASCII/Binary mode</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenAppendURI.cs" lang="cs" /></example>
		public static Stream OpenAppend(Uri uri, bool checkcertificate, FtpDataType datatype) {
			FtpClient cl = null;

			CheckURI(uri);

			cl = Connect(uri, checkcertificate);
			cl.EnableThreadSafeDataConnections = false;

			return cl.OpenAppend(uri.PathAndQuery, datatype);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <param name="checkcertificate">Indicates if a ssl certificate should be validated when using FTPS schemes</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenAppendURI.cs" lang="cs" /></example>
		public static Stream OpenAppend(Uri uri, bool checkcertificate) {
			return OpenAppend(uri, checkcertificate, FtpDataType.Binary);
		}

		/// <summary>
		/// Opens a stream to the file specified by the URI
		/// </summary>
		/// <param name="uri">FTP/FTPS URI pointing at a file</param>
		/// <returns>Stream object</returns>
		/// <example><code source="..\Examples\OpenAppendURI.cs" lang="cs" /></example>
		public static Stream OpenAppend(Uri uri) {
			return OpenAppend(uri, true, FtpDataType.Binary);
		}

		private static void CheckURI(Uri uri) {

			if (string.IsNullOrEmpty(uri.PathAndQuery)) {
				throw new UriFormatException("The supplied URI does not contain a valid path.");
			}

			if (uri.PathAndQuery.EndsWith("/")) {
				throw new UriFormatException("The supplied URI points at a directory.");
			}
		}

		/// <summary>
		/// Calculate you public internet IP using the ipify service. Returns null if cannot be calculated.
		/// </summary>
		/// <returns>Public IP Address</returns>
		public static string GetPublicIP() {
#if NETFX
			try {
				var request = WebRequest.Create("https://api.ipify.org/");
				request.Method = "GET";

				using (var response = request.GetResponse()) {
					using (var stream = new StreamReader(response.GetResponseStream())) {
						return stream.ReadToEnd();
					}
				}
			} catch (Exception) { }
#endif
			return null;
		}

		#endregion

	}
}