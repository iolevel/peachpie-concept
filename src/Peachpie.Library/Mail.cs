using System;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Net.Security;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Pchp.Core;
using Pchp.Core.Utilities;

namespace Pchp.Library
{
    [PhpExtension(PhpExtensionAttribute.KnownExtensionNames.Standard)]
    public static class Mail
    {
        public static bool mail(Context ctx, string to, string subject, string message, string additional_headers = null, string additional_parameters = null)
        {
            // to and subject cannot contain newlines, replace with spaces
            to = (to != null) ? to.Replace("\r\n", " ").Replace('\n', ' ') : "";
            subject = (subject != null) ? subject.Replace("\r\n", " ").Replace('\n', ' ') : "";

            Debug.WriteLine("MAILER", "mail('{0}','{1}','{2}','{3}')", to, subject, message, additional_headers);

            var config = ctx.Configuration.Core;

            // additional_parameters
            additional_parameters = config.ForceExtraMailParameters ?? additional_parameters;

            // set SMTP server we are using
            var client = new RawSmtpClient(config.SmtpServer, config.SmtpPort);

            // X-PHP-Originating-Script
            if (config.AddXHeader)
            {
                additional_headers = "X-PHP-Originating-Script: 1:" + ctx.MainScriptFile.Path + "\n" + additional_headers;
            }

            try
            {
                client.Connect();
                client.SendMessage(
                    config.DefaultFromHeader, to,
                    subject,
                    additional_headers,
                    message);
                return true;
            }
            catch (Exception e)
            {
                string error_message = e.Message;
                Exception inner = e;
                while ((inner = inner.InnerException) != null)
                    error_message += "; " + inner.Message;

                PhpException.Throw(PhpError.Warning, Resources.LibResources.cannot_send_email, error_message);
                return false;
            }
            finally
            {
                client.Disconnect();
            }
        }

        /// <summary>
		/// Counts hash value needed by EZMLM.
		/// </summary>
		/// <param name="addr">Mail address for which is hash value calculating.</param>
		/// <returns>Calculated hash value.</returns>
		public static int ezmlm_hash(string addr)
        {
            // this algorithm is assumed from PHP source code

            uint h = 5381; // must be 32-bit unsigned
            addr = addr.ToLower();

            unchecked // overflow may occur, this is OK.
            {
                for (int j = 0; j < addr.Length; j++)
                {
                    h = (h + (h << 5)) ^ (uint)addr[j];
                }
            }

            h = (h % 53);

            return (int)h;
        }

        #region Mail headers parsing

        ///// <summary>
        ///// Extracts mail headers from string <c>headers</c> and if the string contains supported headers,
        ///// appropriate fields are set to <c>MailMessage mm</c> object.
        ///// Supported headers are: Cc, Bcc, From, Priority, Content-type. Others are ignored.
        ///// </summary>
        ///// <param name="headers">String containing mail headers.</param>
        ///// <param name="mm">MailMessage object to set fields according to <c>headers</c>.</param>
        //static void SetMailHeaders(string headers, MailMessage mm)
        //{
        //    // parse additional headers
        //    Regex headerRegex = new Regex("^([^:]+):[ \t]*(.+)$");
        //    Match headerMatch;

        //    int line_begin, line_end = -1;
        //    while (true)
        //    {
        //        line_begin = line_end + 1;

        //        // search for non-empty line
        //        while (line_begin < headers.Length && (headers[line_begin] == '\n' || headers[line_begin] == '\r'))
        //            line_begin++;
        //        if (line_begin >= headers.Length)
        //            break;

        //        // find the line end
        //        line_end = line_begin + 1;
        //        while (line_end < headers.Length && headers[line_end] != '\n' && headers[line_end] != '\r')
        //            line_end++;

        //        string header = headers.Substring(line_begin, line_end - line_begin);
        //        headerMatch = headerRegex.Match(header);

        //        // ignore wrong formatted headers
        //        if (!headerMatch.Success)
        //            continue;

        //        string sw = headerMatch.Groups[1].Value.Trim().ToLower();
        //        switch (sw)
        //        {
        //            case "cc":
        //                mm.CC.Add(ExtractMailAddressesOnly(headerMatch.Groups[2].Value, Int32.MaxValue));
        //                break;
        //            case "bcc":
        //                mm.Bcc.Add(ExtractMailAddressesOnly(headerMatch.Groups[2].Value, Int32.MaxValue));
        //                break;
        //            case "from":
        //                string from = ExtractMailAddressesOnly(headerMatch.Groups[2].Value, 1);
        //                if (!string.IsNullOrEmpty(from))
        //                {
        //                    try
        //                    {
        //                        mm.From = new MailAddress(from);
        //                    }
        //                    catch (FormatException)
        //                    { }
        //                }
        //                break;
        //            case "priority":
        //                mm.Priority = ExtractPriority(headerMatch.Groups[2].Value);
        //                break;
        //            case "content-type":
        //                ExtractContentType(headerMatch.Groups[2].Value, mm);
        //                break;

        //            default:
        //                mm.Headers.Add(headerMatch.Groups[1].Value.Trim(), headerMatch.Groups[2].Value);
        //                break;
        //        }
        //    }
        //}

        ///// <summary>
        ///// Converts semicolon separated list of email addresses and names of email owners
        ///// to semicolon separated list of only email addresses.
        ///// </summary>
        ///// <param name="emails">Semicolon separated list of email addresses and names.</param>
        ///// <param name="max">Max number of emails returned.</param>
        ///// <returns>Semicolon separated list of email addresses only.</returns>
        //static string ExtractMailAddressesOnly(string emails, int max)
        //{
        //    var mailsOnly = new StringBuilder();
        //    var regWithName = new Regex("^[ \t]*([^<>]*?)[ \t]*<[ \t]*([^<>]*?)[ \t]*>[ \t]*$");
        //    var regEmail = new Regex("^[ \t]*[^@ \t<>]+@[^@ \t<>]+.[^@ \t<>]+[ \t]*$");

        //    Match m, m2;
        //    string toAppend = "";
        //    string[] mailsArray = emails.Split(';');
        //    foreach (string mail in mailsArray)
        //    {
        //        m = regWithName.Match(mail);
        //        if (m.Success) // mail with name
        //        {
        //            Group gr;
        //            for (int i = 1; i < m.Groups.Count; i++)
        //            {
        //                gr = m.Groups[i];
        //                m2 = regEmail.Match(gr.Value);
        //                if (m2.Success)
        //                {
        //                    toAppend = m2.Value;
        //                }
        //            }
        //            // if an e-mail is in <..> we forget previous email found out of <..> (the name looks like e-mail address)
        //            mailsOnly.Append(toAppend);
        //            mailsOnly.Append(';');
        //        }
        //        else
        //        {
        //            m2 = regEmail.Match(mail);
        //            if (m2.Success) // only email without name
        //            {
        //                mailsOnly.Append(m2.Value);
        //                mailsOnly.Append(';');
        //            }
        //            else
        //            {
        //                // bad e-mail address
        //                PhpException.Throw(PhpError.Warning, Resources.LibResources.invalid_email_address, mail);
        //            }
        //        }
        //    }

        //    if (mailsOnly.Length == 0)
        //        return "";

        //    // return without last semicolon
        //    return mailsOnly.ToString(0, mailsOnly.Length - 1);
        //}

        ///// <summary>
        ///// Used for converting header Priority to <c>MailPriority</c> value needed by .NET Framework mailer.
        ///// </summary>
        ///// <param name="p">"Priority:" header value.</param>
        ///// <returns><c>MailPriority</c> specified by header value.</returns>
        //static MailPriority ExtractPriority(string p)
        //{
        //    switch (p.Trim().ToLowerInvariant())
        //    {
        //        case "high":
        //            return MailPriority.High;
        //        case "low":
        //            return MailPriority.Low;
        //        case "normal":
        //        default:
        //            return MailPriority.Normal;
        //    }
        //}

        ///// <summary>
        ///// Used for converting header ContentType to <c>MailFormat</c> value and <c>Encoding</c> class.
        ///// </summary>
        ///// <param name="contentTypeHeader">"Content-type:" header value</param>
        ///// <param name="mm">Mail message instance.</param>
        //static void ExtractContentType(string contentTypeHeader, MailMessage mm)
        //{
        //    contentTypeHeader = contentTypeHeader.Trim().ToLower();

        //    // extract content-type value parts (type/subtype; parameter1=value1; parameter2=value2)
        //    string[] headerParts = contentTypeHeader.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);

        //    if (headerParts == null || headerParts.Length == 0)
        //        return;

        //    // process type/subtype
        //    mm.IsBodyHtml = (headerParts[0].Trim() == "text/html");

        //    for (int i = 1; i < headerParts.Length; ++i)
        //    {
        //        int asspos = headerParts[i].IndexOf('=');
        //        if (asspos < 1) continue;

        //        string propertyName = headerParts[i].Remove(asspos).Trim();
        //        string propertyValue = headerParts[i].Substring(asspos + 1).Trim(new char[] { ' ', '\t', '\"', '\'', '\n', '\r' });

        //        switch (propertyName)
        //        {
        //            case "charset":
        //                try
        //                {
        //                    mm.BodyEncoding = Encoding.GetEncoding(propertyValue);
        //                }
        //                catch (Exception)
        //                { }
        //                break;
        //            default:
        //                break;
        //        }
        //    }

        //    // add header into the mail message as it is
        //    mm.Headers.Add("content-type", contentTypeHeader);
        //}

        #endregion

        #region RawSmtpClient

        /// <summary>
        /// Raw SMTP client serving the needs of PHP mail functions. This is reimplemented mainly because .NET SmtpClient provides
        /// certain level of abstraction which is incompatible with mail function usage. Currently not as much advanced, but it can easily be.
        /// </summary>
        internal class RawSmtpClient
        {
            /// <summary>
            /// Internal exception.
            /// </summary>
            sealed class RawSmtpException : Exception
            {
                public RawSmtpException(string message)
                    : base(message)
                { }
            }

            /// <summary>
            /// Wait time for Socket.Poll - in microseconds.
            /// </summary>
            private const int _pollTime = 100000;

            /// <summary>
            /// Timeout of connection. We don't want to block for too long.
            /// </summary>
            private const int _connectionTimeout = 5000;

            /// <summary>
            /// Gets a value indicating whether this client is connected to a server.
            /// </summary>
            public bool Connected { get { return _connected; } }
            private bool _connected;

            /// <summary>
            /// Gets or sets a value indicating whether this client should implicitly use ESMTP to connect to the server.
            /// </summary>
            public bool UseExtendedSmtp { get { return _useExtendedSmtp; } }
            private bool _useExtendedSmtp;

            /// <summary>
            /// Gets host name set for this client to connect to.
            /// </summary>
            public string/*!*/HostName { get { return _hostName; } }
            private readonly string/*!*/_hostName;

            /// <summary>
            /// Gets port number set for this client to connect to.
            /// </summary>
            public int Port { get { return _port; } }
            private readonly int _port;

            /// <summary>
            /// Gets a list of SMTP extensions supported by current connection.
            /// </summary>
            public string[] Extensions { get { return _extensions; } }
            private string[] _extensions;

            private TextReader _reader;
            private TextWriter _writer;

            private Socket _socket;
            private NetworkStream _stream;

            public RawSmtpClient(string hostName)
                : this(hostName, 25)
            {
            }

            /// <summary>
            /// Initializes a new instance of AdvancedSmtp client class.
            /// </summary>
            /// <param name="hostName">Host name (IP or domain name) of the SMTP server.</param>
            /// <param name="port">Port on which SMTP server runs.</param>
            public RawSmtpClient(string hostName, int port)
            {
                _hostName = hostName ?? string.Empty;
                _port = port;
                _connected = false;
                _useExtendedSmtp = true;
            }

            /// <summary>
            /// Resets the state of this object.
            /// </summary>
            private void ResetConnection()
            {
                if (_reader != null)
                {
                    _reader.Dispose();
                    _reader = null;
                }

                if (_writer != null)
                {
                    _writer.Dispose();
                    _writer = null;
                }

                if (_stream != null)
                {
                    _stream.Dispose();
                    _stream = null;
                }

                if (_socket != null)
                {
                    if (_socket.Connected)
                        _socket.Shutdown(SocketShutdown.Both);
                    _socket.Dispose();
                    _socket = null;
                }

                _extensions = null;
                _connected = false;
            }

            /// <summary>
            /// Connects to the server.
            /// </summary>
            /// <remarks>Method throws an exception on any error.</remarks>
            /// <exception cref="RawSmtpException">If any error occures.</exception>
            public void Connect()
            {
                // invariant condition
                Debug.Assert(_connected == (_socket != null));

                // check whether socket is not already connected
                if (_connected)
                {
                    // check whether the socket is OK
                    bool error = _socket.Poll(_pollTime, SelectMode.SelectError);

                    if (!error)
                        // ok, we keep this connection
                        return;// true;

                    // close the socket and reset
                    ResetConnection();
                }

                // resolve host's domain
                IPAddress[] addresses = null;

                try
                {
                    addresses = System.Net.Dns.GetHostAddressesAsync(_hostName).Result;
                }
                catch (Exception e)
                {
                    // DNS error - reset and fail
                    ResetConnection();
                    throw new RawSmtpException(e.Message);
                }

                Debug.Assert(addresses != null);

                // create socket
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                // connect to the remote server
                _socket.ConnectAsync(addresses, _port).Wait(_connectionTimeout);

                // if socket could not connect, reset and fail
                if (!_socket.Connected)
                {
                    ResetConnection();
                    throw new RawSmtpException("Cannot connect to " + _hostName);
                }

                // if anything inside throws exception, we were not successful
                try
                {
                    // create a stream
                    _stream = new NetworkStream(_socket);

                    // create _reader and _writer
                    _reader = new StreamReader(_stream, Encoding.ASCII);
                    _writer = new StreamWriter(_stream, Encoding.ASCII);
                    _writer.NewLine = "\r\n";

                    string line;

                    // read server welcome message
                    line = _reader.ReadLine();

                    // if there is no 220 in the beginning, this is no SMTP server
                    if (!line.StartsWith("220")) throw new RawSmtpException("Expected 220, '" + line + "' given");// return false;
                    //TODO: server name processing

                    // send ESMTP welcome message
                    if (_useExtendedSmtp)
                    {
                        Post("EHLO " + System.Net.Dns.GetHostName());

                        // read response
                        line = _reader.ReadLine();
                    }

                    if (_useExtendedSmtp && line.StartsWith("250"))
                    {
                        // this is ESMTP server

                        // ESMTP returns '-' on fourth char if there are any more lines available
                        if (line[3] == ' ')
                        {
                            // there are no extensions
                            _extensions = ArrayUtils.EmptyStrings;

                            // success
                            return;// true;
                        }
                        else if (line[3] == '-')
                        {
                            List<string> extensions = new List<string>();

                            // we do not need to read first line - there is only a welcome string

                            while (true)
                            {
                                //read new line
                                line = _reader.ReadLine();

                                if (line.StartsWith("250-"))
                                {
                                    //add new extension name
                                    extensions.Add(line.Substring(4, line.Length - 4));
                                }
                                else if (line.StartsWith("250 "))
                                {
                                    //add new extension name and finish handshake
                                    extensions.Add(line.Substring(4, line.Length - 4));
                                    _extensions = extensions.ToArray();
                                    _connected = true;
                                    return;// true;
                                }
                                else
                                {
                                    //invalid response (do not send QUIT message)
                                    break;
                                }
                            }
                        }

                        // this is not a valid ESMTP server
                    }
                    else if (line.StartsWith("500") || !_useExtendedSmtp)
                    {
                        Post("HELO " + System.Net.Dns.GetHostName());

                        if (Ack("250"))
                        {
                            _extensions = ArrayUtils.EmptyStrings;

                            // handshake complete
                            _connected = true;
                            return;// true;
                        }
                    }
                }
                catch (Exception e)
                {
                    throw new RawSmtpException(e.Message);
                } // any error is bad

                ResetConnection(); // (do not send QUIT message)

                throw new RawSmtpException("Unexpected"); //return false;
            }

            /// <summary>
            /// Disconnects the client from the server.
            /// </summary>
            public void Disconnect()
            {
                if (!_connected)
                {
                    ResetConnection();
                    return;
                }

                Post("QUIT");
                Ack("221", null, (_) => {/*incorrect response (do nothing)*/});

                //correct response
                ResetConnection();
            }

            /// <summary>
            /// Sends reset message to the server.
            /// </summary>
            private void Reset()
            {
                if (!_connected) return;

                if (_reader.Peek() != -1)
                {
                    // there is something on the input (should be empty)
                    ResetConnection();
                    return;
                }

                Post("RSET");
                Ack("250", null,
                    (_) => ResetConnection());
            }

            /// <summary>
            /// Starts mail transaction and prepares the data lines from supplied message properties.
            /// Processes provided headers to determine cc, bcc and from values.
            /// All data will be send as ASCII if possible.
            /// </summary>
            /// <param name="from">Sender of the mail.</param>
            /// <param name="to">Recipients of the mail.</param>
            /// <param name="subject">Subject of the mail.</param>
            /// <param name="headers">Additional headers.</param>
            /// <param name="body">Message body.</param>
            /// <returns>List of message body lines.</returns>
            private IEnumerable<string>/*!*/ProcessMessageHeaders(string from, string to, string subject, string headers, string body)
            {
                Dictionary<string, int> headerHashtable = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                List<KeyValuePair<string, string>> headerList = new List<KeyValuePair<string, string>>();
                List<string> recipients = new List<string>(1) { to };

                //parse headers
                if (headers != null)
                    using (StringReader reader = new StringReader(headers))
                    {
                        string line;
                        while ((line = reader.ReadLine()) != null)
                        {
                            int index = line.IndexOf(": ", StringComparison.Ordinal);

                            if (index > 0)
                            {
                                string name = line.Substring(0, index);
                                string value = line.Substring(index + 2);

                                //
                                headerHashtable[name] = headerList.Count;   // remember last position of <name> header
                                headerList.Add(new KeyValuePair<string, string>(name, value));

                                // process known headers:
                                if (from == null && name.Equals("from", StringComparison.OrdinalIgnoreCase))
                                    from = value;
                                if (name.Equals("cc", StringComparison.OrdinalIgnoreCase) || name.Equals("bcc", StringComparison.OrdinalIgnoreCase))
                                    recipients.Add(value); //PostRcptTo(value); // postponed until we are discovering from address
                            }
                        }
                    }

                // check from address:
                if (from == null)
                    throw new RawSmtpException(Resources.LibResources.smtp_sendmail_from_not_set);

                // start mail transaction:
                Post(FormatEmailAddress(from, "MAIL FROM:<{0}>"));
                Ack("250");

                for (int i = 0; i < recipients.Count; i++)
                    PostRcptTo(recipients[i]);

                // additional message lines:
                List<string> ret = new List<string>();

                // Date:
                ret.Add("Date: " + System.DateTime.Now.ToString("ddd, dd MMM yyyy HH:mm:ss zz00", new System.Globalization.CultureInfo("en-US")));

                // From: // Only add the From: field from <from> parameter if it isn't in the custom headers:
                if (!headerHashtable.ContainsKey("from") && !string.IsNullOrEmpty(from))
                    ret.Add("From: " + from);

                // Subject:
                ret.Add("Subject: " + (subject ?? "No Subject"));

                // To: // Only add the To: field from the <to> parameter if isn't in the custom headers:
                if (!headerHashtable.ContainsKey("to") && !string.IsNullOrEmpty(to))
                    ret.Add("To: " + to);

                // add headers, ignore duplicities (only accept the last occurance):
                foreach (var headerIndex in headerHashtable.Values)
                {
                    var header = headerList[headerIndex];
                    ret.Add(string.Format("{0}: {1}", header.Key, header.Value));
                }

                ret.Add("");

                // parse the <body> into lines:
                var bodyReader = new StringReader(body);

                while (bodyReader.Peek() != -1)
                    ret.Add(bodyReader.ReadLine());

                return ret;
            }

            /// <summary>
            /// Cut out the address if contained within &lt;...&gt; characters. Otherwise take the whole <paramref name="address"/> string.
            /// The address is transformed using given <paramref name="formatString"/> format.
            /// </summary>
            /// <param name="address">Given mail address.</param>
            /// <param name="formatString">Format to be used.</param>
            /// <returns>Formatted email address.</returns>
            private static string FormatEmailAddress(string/*!*/address, string/*!*/formatString)
            {
                Debug.Assert(address != null, "address == null");
                Debug.Assert(formatString != null, "formatString == null");

                int a, b;
                if ((a = address.IndexOf('<')) >= 0 && (b = address.IndexOf('>', a)) >= 0)
                    address = address.Substring(a + 1, b - a - 1);

                return string.Format(formatString, address.Trim());
            }

            #region Post, Ack

            /// <summary>
            /// Writes <paramref name="line"/>, appends <c>CRLF</c> and flushes internal writer.
            /// </summary>
            /// <param name="line"><see cref="String"/> to be written onto the internal writer.</param>
            private void Post(string line)
            {
                this._writer.WriteLine(line);
                this._writer.Flush();
            }

            private bool Ack(string expected1)
            {
                return Ack(expected1, null,
                    (line) => ThrowExpectedResponseHelper(line, expected1));
            }

            private bool Ack(string expected1, string expected2)
            {
                return Ack(expected1, expected2,
                    (line) => ThrowExpectedResponseHelper(line, string.Format("{0} or {1}", expected1, expected2)));
            }

            private void ThrowExpectedResponseHelper(string givenResponse, string expectedStr)
            {
                Reset();
                throw new RawSmtpException(string.Format("Expected response {0}, '{1}' given.", expectedStr, givenResponse));
            }

            private bool Ack(string expected1, string expected2, Action<string>/*!*/fail)
            {
                Debug.Assert(fail != null);

                var line = _reader.ReadLine();

                if (expected1 != null && line.StartsWith(expected1, StringComparison.Ordinal))
                    return true; // ok

                if (expected2 != null && line.StartsWith(expected2, StringComparison.Ordinal))
                    return true; // ok

                fail(line);
                return false;
            }

            #endregion

            /// <summary>
            /// Send <c>RCPT TO</c> commands.
            /// </summary>
            /// <param name="recipients">List of recipients comma-separated.</param>
            private void PostRcptTo(string recipients)
            {
                if (!string.IsNullOrEmpty(recipients))
                    foreach (var rcpt in recipients.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        if (rcpt.StartsWith("undisclosed-recipients:", StringComparison.Ordinal))
                            continue;   // this should be specified in To: header, it is not intended for the SMTP server within RCPT TO command

                        Post(FormatEmailAddress(rcpt, "RCPT TO:<{0}>"));
                        Ack("250", "251");
                    }
            }

            /// <summary>
            /// Sends the raw message.
            /// </summary>
            /// <remarks>On eny error an exception is thrown.</remarks>
            /// <exception cref="RawSmtpException">When any error occures during the mail send.</exception>
            public void SendMessage(string from, string to, string subject, string headers, string body)
            {
                //
                // see http://email.about.com/cs/standards/a/smtp_error_code_2.htm for response codes.
                //

                if (!_connected)
                    throw new RawSmtpException("NOT CONNECTED");

                // start mail transaction and
                // process headers (may contain additional recipients and from address)
                // and prepare data that is broken up to form data lines.
                // Note ProcessMessageData may add additional recipients, so it must be called before "DATA" section.
                var dataLines = ProcessMessageHeaders(from, to, subject, headers, body);

                // send DATA
                Post("DATA");
                Ack("354");

                foreach (string dataLine in dataLines)
                {
                    // PHP implementation uses 991 line length limit (including CRLF)
                    const int maxLineLength = 989;
                    int lineStart = 0;
                    int correction = 0;

                    // if SP character is on the first place, we need to duplicate it
                    if (dataLine.Length > 0 && dataLine[0] == '.')
                        _writer.Write('.');

                    // according to MIME, the lines must not be longer than 998 characters (1000 including CRLF)
                    // so we need to break such lines using folding
                    while (dataLine.Length - lineStart > maxLineLength - correction)
                    {
                        //break the line, inserting FWS sequence
                        _writer.WriteLine(dataLine.Substring(lineStart, maxLineLength - correction));
                        _writer.Write(' ');
                        lineStart += maxLineLength - correction;

                        //make correction (whitespace on the next line)
                        correction += 1;
                    }

                    //output the rest of the line
                    _writer.WriteLine(dataLine.Substring(lineStart));

                    // flush the stream
                    _writer.Flush();
                }

                _writer.WriteLine(".");

                // flush the stream
                _writer.Flush();

                Ack("250");

                //return true; // ok
            }
        }

        #endregion
    }

    //[PhpExtension("IMAP")] // uncomment when the extension is ready
    public static class Imap
    {
        #region Constants
        readonly static Encoding ISO_8859_1 = Encoding.GetEncoding("ISO-8859-1");

        public const int CL_EXPUNGE = 32768;
        #endregion

        #region ImapResource
        /// <summary>
        /// Base of protocols POP3, IMAP and NNTP.
        /// </summary>
        internal abstract class MailResource : PhpResource
        {
            private enum Service { IMAP, NNTP, POP3};

            /// <summary>
            /// Represents a connection between a client and server.
            /// </summary>
            protected Stream _stream;
            /// <summary>
            /// Represents an initial connection string.
            /// </summary>
            protected MailBoxInfo _info;

            #region Contructors
            protected MailResource() : base("imap") { }

            /// <summary>
            /// Creates a client for one of three supported protocols. 
            /// </summary>
            /// <param name="info">A connection string, which contains info about desired protocol. Default is IMAP.</param>
            /// <returns>The client of desired protocol or NULL if there is a problem with the connection.</returns>
            public static MailResource Create(MailBoxInfo info)
            {
                if (info == null)
                    return null;

                MailResource result = null;
                Stream stream = GetStream(info);
                if (stream == null)
                    return null;

                if (String.IsNullOrEmpty(info.Service))
                {
                    if (info.NameFlags.Contains("imap") || info.NameFlags.Contains("imap2") || info.NameFlags.Contains("imap2bis")
                        || info.NameFlags.Contains("imap4") || info.NameFlags.Contains("imap4rev1"))
                        result = CreateImap(info, stream);
                    else if (info.NameFlags.Contains("pop3"))
                        result = CreatePop3(info, stream);
                    else if (info.NameFlags.Contains("nntp"))
                        result = CreateNntp(info, stream);
                    else // Default is imap                   
                        result = CreateImap(info, stream);
                }
                else
                {

                    switch (info.Service)
                    {
                        case "pop3":
                            result = CreatePop3(info, stream);
                            break;
                        case "nntp":
                            result = CreateNntp(info, stream);
                            break;
                        default: // Default is imap
                            result = CreateImap(info, stream);
                            break;
                    }
                }

                return result;
            }

            /// <summary>
            /// Creates IMAP client with a specific type of connection(TLS, SSL, ...).
            /// </summary>
            /// <param name="info">Info can contain information about a type of connection(security, tls, ...)</param>
            /// <param name="stream">A stream which is connected to a server.</param>
            /// <returns>Returns the client or null, if there is problem with the connection.</returns>
            private static ImapResource CreateImap(MailBoxInfo info, Stream stream)
            {
                if (info == null || stream == null)
                    return null;

                ImapResource resource = ImapResource.Create(GetStream(info));
                if (resource == null)
                    return null;

                resource._info = info; // Save info about connection

                if (info.NameFlags.Contains("secure")) // StartTLS with validation
                    resource.StartTLS(true);
                else if (info.NameFlags.Contains("tls"))// StartTLS
                    resource.StartTLS(!info.NameFlags.Contains("novalidate-cert"));

                return resource;
            }

            /// <summary>
            /// Creates POP3 client with a specific type of connection(TLS, SSL, ...)
            /// </summary>
            /// <param name="info">Info can contain information about a type of connection(security, tls, ...)</param>
            /// <param name="stream">A stream which is connected to a server.</param>
            /// <returns>Returns the client or null, if there is problem with the connection.</returns>
            private static POP3Resource CreatePop3(MailBoxInfo info, Stream stream)
            {
                if (info == null || stream == null)
                    return null;

                POP3Resource resource = POP3Resource.Create(GetStream(info));
                if (resource == null)
                    return null;

                resource._info = info; // Save info about connection

                if (info.NameFlags.Contains("secure")) // StartTLS with validation
                    resource.StartTLS(true);
                else if (info.NameFlags.Contains("tls"))// StartTLS
                    resource.StartTLS(!info.NameFlags.Contains("novalidate-cert"));

                return resource;
            }

            /// <summary>
            /// Creates NNTP client with a specific type of connection(TLS, SSL, ...)
            /// </summary>
            /// <param name="info">Info can contain information about a type of connection(security, tls, ...)</param>
            /// <param name="stream">A stream which is connected to a server.</param>
            /// <returns>Returns the client or null, if there is problem with the connection.</returns>
            private static ImapResource CreateNntp(MailBoxInfo info, Stream stream)
            {
                if (info == null || stream == null)
                    return null;

                //TODO: Support for NNTP protocol
                throw new NotImplementedException();
            }

            /// <summary>
            /// Creates a stream which is connected to a server. Makes no-secure conection by default.
            /// </summary>
            /// <param name="info">Info which contains info about server address</param>
            /// <returns>Stream or null, if there is a problem with a connetion.</returns>
            private static Stream GetStream(MailBoxInfo info)
            {
                if (info == null)
                    return null;

                TcpClient client;
                try
                {
                    client = new TcpClient(info.Hostname, info.Port);
                }
                catch (Exception ex)
                {
                    if (ex is ArgumentNullException || ex is SocketException)
                        return null;

                    throw;
                }


                if (info.NameFlags.Contains("notls")) // There is nothing to do yet and return non-secure connection (can be later change by starttls command)
                    return client.GetStream();

                if (info.NameFlags.Contains("ssl"))
                    return MakeSslConection(client.GetStream(), info);

                return client.GetStream(); // Makes no-secure conection by default.
            }

            /// <summary>
            /// Makes authentication and ssl conection according to settings in info.
            /// </summary>
            /// <param name="stream">A stream which is connected to server.</param>
            /// <param name="info">The information about connection string.</param>
            /// <returns>Sslstream or null, if there is a problem with authentication.</returns>
            protected static SslStream MakeSslConection(Stream stream, MailBoxInfo info)
            {
                SslStream result;
                try
                {
                    if (info.NameFlags.Contains("novalidate-cert"))
                        result = new SslStream(stream, false, (sender, certificate, chain, sslPolicyErrors) => true);
                    else // Validate Certificate
                        result = new SslStream(stream, false, ValidateServerCertificate);

                    result.AuthenticateAsClient(info.Hostname);
                }
                catch (AuthenticationException)
                {
                    return null;
                }

                return result;
            }

            /// <summary>
            /// Validates a certificate. Copied from https://docs.microsoft.com/en-us/dotnet/api/system.net.security.sslstream?view=netcore-3.1. 
            /// </summary>
            /// <returns>Returns true if <see cref="SslPolicyErrors"/> is equal to None, false otherwise.</returns>
            protected static bool ValidateServerCertificate(object sender, X509Certificate certificate, X509Chain chain, SslPolicyErrors sslPolicyErrors)
            {
                if (sslPolicyErrors == SslPolicyErrors.None)
                    return true;

                // Refuse connection
                return false;
            }
            #endregion

            #region Methods
            /// <summary>
            /// Receive bytes. The bytes represents responses from server and has to be ended \r\n.
            /// </summary>
            /// <param name="wait">Set false, if you don't want to wait until response arrived.</param>
            /// <returns>Returns bytes ended by \r\n, or null if there is no response and you don't want to wait for response.</returns>
            protected byte[] ReceiveBytes(bool wait = true)
            {
                byte[] buffer = new byte[2];
                int length = 0;

                if (!wait)
                {
                    _stream.ReadTimeout = 2;
                    try
                    {
                        length = _stream.Read(buffer, 0, buffer.Length);
                    }
                    catch (TimeoutException)
                    {
                        return null;
                    }
                    finally
                    {
                        _stream.ReadTimeout = -1;
                    }
                }
                else
                {
                    length = _stream.Read(buffer, 0, buffer.Length);
                }

                //Wait for complete message.
                while (buffer[length - 2] != '\r' || buffer[length - 1] != '\n')
                {
                    Task.Delay(1);
                    int bufferSize = 1024;

                    byte[] newBuffer = new byte[buffer.Length + bufferSize];
                    length = _stream.Read(newBuffer, buffer.Length, bufferSize);
                    Buffer.BlockCopy(buffer, 0, newBuffer, 0, buffer.Length);
                    length = length + buffer.Length;
                    buffer = newBuffer;
                }

                return buffer;
            }
            protected abstract bool StartTLS(bool sslValidation);
            public abstract bool Login(string username, string password);
            public abstract bool Close();
            #endregion
        }

        /// <summary>
        /// Context of POP3 session.
        /// </summary>
        internal class POP3Resource : MailResource
        {
            /// <summary>
            /// Represents a status of received message. There are two types in POP3.
            /// </summary>
            private enum Status { OK, ERR, None};

            /// <summary>
            /// Represents a response from a server. 
            /// </summary>
            class POP3Response
            {
                /// <summary>
                /// Status of sent command.
                /// </summary>
                public Status Status { get; set; } = Status.None;
                /// <summary>
                /// The rest of message.
                /// </summary>
                public string Body { get; set; } = null;
                /// <summary>
                /// Complete message delimeted by \r\n sequence(Common ending sequence in POP3).
                /// </summary>
                public byte[] Raw { get; set; } = null;
                /// <summary>
                /// Tries to parse a server response.
                /// </summary>
                /// <param name="buffer">Buffer, which contains one message ended by \r\n.</param>
                /// <param name="response">The result.</param>
                /// <returns> If the header hasn't the standard form, Only Raw property is filled.</returns>
                public static bool TryParse(byte[] buffer, out POP3Response response)
                {
                    response = new POP3Response();
                    if (buffer == null)
                        return false;

                    int index = 0;

                    // Status
                    if (buffer.Length >= 3 && buffer[index] == OkTag[0] && buffer[index + 1] == OkTag[1] && buffer[index + 2] == OkTag[2])
                    {
                        response.Status = Status.OK;
                        index = index + 3;
                    }
                    else if (buffer.Length >= 4 && buffer[index] == ErrTag[0] && buffer[index + 1] == ErrTag[1] && buffer[index + 2] == ErrTag[2] && buffer[index + 3] == ErrTag[3])
                    {
                        response.Status = Status.ERR;
                        index = index + 4;
                    }
                    else
                    {
                        response.Status = Status.None;
                    }

                    if (index < buffer.Length && buffer[index] == ' ')
                        index++;

                    // Body
                    response.Body = Encoding.ASCII.GetString(buffer, index, buffer.Length - index);
                    response.Raw = buffer;

                    return true;
                }
            }

            #region Constants
            const string OkTag = "+OK";
            const string ErrTag = "-ERR";
            #endregion

            #region Constructors

            /// <summary>
            /// Creates IMAP client.
            /// </summary>
            /// <param name="stream">A stream which is connected to server.</param>
            /// <returns>Returns the client or null if there is problem with receiving an initial message.</returns>
            public static POP3Resource Create(Stream stream)
            {
                POP3Resource resource = new POP3Resource();
                resource._stream = stream;

                List<POP3Response> responses = resource.Receive();

                return (responses != null && responses.Count != 0 && responses[0].Status == Status.OK) ? resource : null;
            }
            #endregion

            #region Methods
            /// <summary>
            /// Executes the command STLS.
            /// </summary>
            /// <param name="sslValidation">Set false if you don't want certificate validation.</param>
            /// <returns>Returns true on success, false otherwise.</returns>
            protected override bool StartTLS(bool sslValidation)
            {
                string command = $"STLS\r\n";
                Write(command);

                List<POP3Response> responses = Receive();
                    if (responses[0].Status != Status.OK) //It should be the first message.
                        return false;

                var stream = MakeSslConection(_stream, _info);
                if (stream == null)
                {
                    return false;
                }
                else
                {
                    _stream = stream;
                    return true;
                }
            }

            /// <summary>
            /// Writes command into stream.
            /// </summary>
            /// <param name="command">An POP3 command.</param>
            private void Write(string command)
            {
                _stream.Write(Encoding.ASCII.GetBytes(command));
            }

            /// <summary>
            /// Receives a response from server. Server can send more than one response.
            /// </summary>
            /// <param name="wait">Set false, if you don't want to wait until response arrived.</param>
            /// <returns>Returns response(s), or null if there is no response and you don't want to wait for response.</returns>
            private List<POP3Response> Receive(bool wait = true)
            {
                byte[] buffer = ReceiveBytes(wait);
                if (buffer == null)
                    return null;

                List<POP3Response> responses = new List<POP3Response>();
                int startIndex = 0;
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == '\n' && buffer[i - 1] == '\r') // Split the message (Server's responses are ended by \r\n sequence) 
                    {
                        if (POP3Response.TryParse(buffer.Slice(startIndex, i - startIndex + 1), out POP3Response pop3))
                            responses.Add(pop3);

                        startIndex = i + 1;
                    }
                }

                return responses;
            }

            /// <summary>
            /// Executes commands USER {username} and PASS {password.}
            /// </summary>
            /// <returns>Returns true on success or false on failure.</returns>
            public override bool Login(string username, string password)
            {
                Write($"USER {username}\r\n");

                List<POP3Response> responses = Receive();
                if (responses == null || responses.Count == 0 || responses[0].Status != Status.OK)
                    return false;

                Write($"PASS {password}\r\n");
                
                responses = Receive();
                return ((responses != null || responses.Count != 0) && responses[0].Status == Status.OK);
            }

            /// <summary>
            /// Executes QUIT and calls FreeManaged.
            /// </summary>
            public override bool Close()
            {
                Write($"QUIT\r\n");

                List<POP3Response> responses = Receive();
                bool result = ((responses != null || responses.Count != 0) && responses[0].Status == Status.OK);

                if (result)
                    FreeManaged();

                return result;
            }

            protected override void FreeManaged()
            {
                _stream.Close();
                _stream.Dispose();
                base.FreeManaged();
            }
            #endregion
        }

        /// <summary>
        /// Context of NNTP session.
        /// </summary>
        internal class NNTPResource : MailResource
        {
            public override bool Close()
            {
                throw new NotImplementedException();
            }

            public override bool Login(string username, string password)
            {
                throw new NotImplementedException();
            }

            protected override bool StartTLS(bool sslValidation)
            {
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Context of IMAP session.
        /// </summary>
        internal class ImapResource : MailResource
        {
            /// <summary>
            /// Represents a status of received message. There are three types in IMAP.
            /// </summary>
            private enum Status { OK, NO, BAD, None };

            /// <summary>
            /// Represents a response from a server. 
            /// </summary>
            class ImapResponse
            {
                /// <summary>
                /// Tag used by IMAP for monitoring status of a command.
                /// </summary>
                public string Tag { get; set; } = null;
                /// <summary>
                /// Status of sent command.
                /// </summary>
                public Status Status { get; set; } = Status.None;
                /// <summary>
                /// The rest of message.
                /// </summary>
                public string Body { get; set; } = null;
                /// <summary>
                /// Complete message delimeted by \r\n sequence(Common ending sequence in IMAP).
                /// </summary>
                public byte[] Raw { get; set; } = null;

                /// <summary>
                /// Tries to parse a server response.
                /// </summary>
                /// <param name="buffer">Buffer, which contains one message ended by \r\n.</param>
                /// <param name="response">The result.</param>
                /// <returns> If the header hasn't the standard form, Only Raw property is filled.</returns>
                public static bool TryParse(byte[] buffer, out ImapResponse response)
                {
                    response = new ImapResponse();
                    if (buffer == null)
                        return false;

                    int index = 0;

                    // Tag property
                    if (buffer[index] == UnTaggedTag)
                    {
                        response.Tag = UnTaggedTag.ToString();
                        index++;
                    }
                    else if (buffer[index] == ContinousTag)
                    {
                        response.Tag = ContinousTag.ToString();
                        index++;
                    }
                    else if (buffer[index] == TagPrefix)
                    {
                        index++;
                        while (index < buffer.Length && buffer[index] >= '0' && buffer[index] <= '9')
                            index++;

                        response.Tag = Encoding.ASCII.GetString(buffer, 0, index);
                    }

                    if (index < buffer.Length && buffer[index] == ' ')
                        index++;

                    // Status property
                    if (index + 1 < buffer.Length)
                    {
                        if (buffer[index] == 'N' && buffer[index + 1] == 'O')
                        {
                            response.Status = Status.NO;
                            index += 2;
                        }
                        else if (buffer[index] == 'O' && buffer[index + 1] == 'K')
                        {
                            response.Status = Status.OK;
                            index += 2;
                        }
                        else if (index + 2 < buffer.Length && buffer[index] == 'D' && buffer[index + 1] == 'A' && buffer[index + 2] == 'D')
                        {
                            response.Status = Status.BAD;
                            index += 3;
                        }
                        else
                            response.Status = Status.None;
                    }

                    // Body property
                    response.Body = Encoding.ASCII.GetString(buffer, index, buffer.Length - index);

                    // Raw property
                    response.Raw = buffer;

                    return true;
                }
            }

            #region Constants
            // Tags belong to responses from a server.
            const char UnTaggedTag = '*';
            const char ContinousTag = '+';
            const char TagPrefix = 'A';
            #endregion

            #region Properties
            /// <summary>
            /// Represents next number of tag, which will be used to send new message to server in format {TagPrefix}{_tag}.
            /// </summary>
            private int _tag = 0;
            #endregion

            #region Constructors
            private ImapResource() {}

            /// <summary>
            /// Creates IMAP client.
            /// </summary>
            /// <param name="stream">A stream which is connected to server.</param>
            /// <returns>Returns the client or null if there is problem with receiving an initial message.</returns>
            public static ImapResource Create(Stream stream)
            {
                ImapResource resource = new ImapResource();
                resource._stream = stream;

                // The server should send an initial message.
                List<ImapResponse> responses = resource.Receive();
                if (responses == null || responses.Count == 0)
                    return null;

                // First message should contain information about connection status.
                return (responses[0].Status == Status.OK) ? resource : null;
            }
            #endregion

            #region Methods

            /// <summary>
            /// Executes the command STARTTLS.
            /// </summary>
            /// <param name="sslValidation">Set false if you don't want certificate validation.</param>
            /// <returns>Returns true on success, false otherwise.</returns>
            protected override bool StartTLS(bool sslValidation = true)
            {
                string messageTag = $"{TagPrefix}{_tag.ToString()}";
                string command = $"{messageTag} STARTTLS\r\n";
                Write(command);

                bool completed = false;
                while (!completed) // Waits until command is completed(Server sends OK message with right messageTag)
                {
                    List<ImapResponse> responses = Receive();
                    if (responses == null || responses.Count == 0)
                        continue;

                    /* Server can send more then one messages. 
                     * We have to find the one, which contains information about command status.
                     * */
                    foreach (var response in responses)
                        if (response.Tag == messageTag) // There has to be message with right tag.
                            if (response.Status != Status.OK) // Returns, if command failed.
                                return false;
                            else
                                completed = true;
                }

                var stream = MakeSslConection(_stream, _info);
                if (stream == null)
                {
                    return false;
                }
                else
                {
                    _stream = stream;
                    return true;
                }
            }

            /// <summary>
            /// Writes command into stream and increment the tag.
            /// </summary>
            /// <param name="command">An IMAP command.</param>
            private void Write(string command)
            {
                _stream.Write(Encoding.ASCII.GetBytes(command));
                _tag++;
            }

            /// <summary>
            /// Receives a response from server. Server can send more than one response.
            /// </summary>
            /// <param name="wait">Set false, if you don't want to wait until response arrived.</param>
            /// <returns>Returns response(s), or null if there is no response and you don't want to wait for response.</returns>
            private List<ImapResponse> Receive(bool wait = true)
            {
                byte[] buffer = ReceiveBytes(wait);
                if (buffer == null)
                    return null;

                List<ImapResponse> responses = new List<ImapResponse>();
                int startIndex = 0;
                for (int i = 1; i < buffer.Length; i++)
                {
                    if (buffer[i] == '\n' && buffer[i - 1] == '\r') // Split the message (Server's responses are ended by \r\n sequence)  
                    {
                        if (ImapResponse.TryParse(buffer.Slice(startIndex, i - startIndex + 1), out ImapResponse imap))
                            responses.Add(imap);

                        startIndex = i + 1;
                    }
                }

                return responses;
            }

            /// <summary>
            /// Executes command LOGIN {username} {password}.
            /// </summary>
            /// <returns>Returns true on success or false on failure.</returns>
            public override bool Login(string username, string password)
            {
                string messageTag = $"{TagPrefix}{_tag.ToString()}";
                Write($"{messageTag} LOGIN {username} {password}\r\n");

                while (true) // Waits for the response.
                {
                    List<ImapResponse> responses = Receive();
                    foreach (var response in responses)
                        if (response.Tag == messageTag)
                            return response.Status == Status.OK;
                }
            }

            /// <summary>
            /// Excutes command SELECT {path}.
            /// </summary>
            /// <param name="path">The path in mailbox.</param>
            /// <returns>Returns true on success or false on failure.</returns>
            public bool Select(string path)
            {
                string messageTag = $"{TagPrefix}{_tag.ToString()}";
                Write($"{messageTag} SELECT {path}\r\n");

                while (true) // Waits for the response.
                {
                    List<ImapResponse> responses = Receive();
                    foreach (var response in responses)
                        if (response.Tag == messageTag)
                            return response.Status == Status.OK;
                }
            }

            /// <summary>
            /// Executes LOGOUT and calls FreeManaged.
            /// </summary>
            public override bool Close()
            {
                string messageTag = $"{TagPrefix}{_tag.ToString()}";
                Write($"{messageTag} LOGOUT\r\n");

                bool result = false;
                bool completed = false;
                while (!completed) // Waits for the response.
                {
                    List<ImapResponse> responses = Receive();
                    foreach (var response in responses)
                        if (response.Tag == messageTag)
                        {
                            result = response.Status == Status.OK;
                            completed = true;
                            break;
                        }
                }

                if (result)
                    FreeManaged();

                return result;
            }

            protected override void FreeManaged()
            {
                _stream.Close();
                _stream.Dispose();
                base.FreeManaged();
            }
            #endregion
        }
        #endregion

        #region Unsorted
        /// <summary>
        /// Gets instance of <see cref="MailResource"/> or <c>null</c>.
        /// If given argument is not an instance of <see cref="MailResource"/>, PHP warning is reported.
        /// </summary>
        static MailResource ValidateMailResource(PhpResource context)
        {
            if (context is MailResource h && h.IsValid)
            {
                return h;
            }

            //
            PhpException.Throw(PhpError.Warning, Resources.Resources.invalid_context_resource);
            return null;
        }

        /// <summary>
        /// Parses an address string.
        /// </summary>
        public static PhpArray imap_rfc822_parse_adrlist(string addresses, string default_host = null)
        {
            if (string.IsNullOrEmpty(addresses))
            {
                return PhpArray.NewEmpty();
            }

            var collection = new MailAddressCollection();
            collection.Add(addresses);

            var arr = new PhpArray(collection.Count);
            foreach (var addr in collection)
            {
                var item = new PhpArray(3)
                {
                    { "mailbox", addr.User },
                    { "host", addr.Host ?? default_host },
                };

                if (addr.DisplayName != null) item["personal"] = addr.DisplayName;

                arr.Add(item.AsStdClass());
            }

            //
            return arr;
        }
        #endregion

        #region encode,decode

        #region utf7
        /// <summary>
        /// Transforms bytes to modified UTF-7 text as defined in RFC 2060
        /// </summary>
        private static string TransformUTF8ToUTF7Modified(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0)
                return string.Empty;

            var builder = StringBuilderUtilities.Pool.Get();

            for (int i = 0; i < bytes.Length; i++)
            {
                // Chars from 0x20 to 0x7e are unchanged excepts "&" which is replaced by "&-".
                if (bytes[i] >= 0x20 && bytes[i] <= 0x7e)
                {
                    if (bytes[i] == 0x26)
                        builder.Append("&-");
                    else
                        builder.Append((char)bytes[i]);
                }
                else // Collects all bytes until Char from 0x20 to 0x7eis reached.
                {
                    int index = i;
                    while ( i < bytes.Length && (bytes[i] < 0x20 || bytes[i] > 0x7e))
                        i++;

                    //Add bytes to stringbuilder
                    //builder.Append("&" + Encoding.UTF8.GetString(bytes, index, i - index).Replace("/", ",") + "-");
                    builder.Append("&" + System.Convert.ToBase64String(bytes, index, i - index).Replace("/", ",") + "-");

                    if (i < bytes.Length)
                        i--;
                }
            }

            return StringBuilderUtilities.GetStringAndReturn(builder);
        }

        private static string TransformUTF7ModifiedToUTF8(Context ctx, string text)
        {
            if (string.IsNullOrEmpty(text))
                return null;

            var builder = StringBuilderUtilities.Pool.Get();

            for (int i = 0; i < text.Length; i++)
            {
                if (text[i] == '&')
                {
                    //if (i == text.Length - 1)
                    //    ; // Error

                    if (text[++i] == '-') // Means "&" char.
                        builder.Append("&");
                    else // Shift
                    {
                        int index = i;
                        while (i < text.Length && text[i] != '-')
                            i++;

                        string encode = text.Substring(index, i - index);
                        if (encode.Length % 4 != 0)
                            encode = encode.PadRight(encode.Length + (4 - encode.Length % 4), '=');
       
                        builder.Append(Encoding.UTF7.GetString(System.Convert.FromBase64String(encode.Replace(",","/"))));
                    }
                }
                else if (text[i] >= 0x20 && text[i] <= 0x7e)
                {
                    builder.Append(text[i]);
                }
                else
                { 
                //Error
                }
            }

            return StringBuilderUtilities.GetStringAndReturn(builder);
        }

        /// <summary>
        /// Converts ctx.StringEncoding encoding to UTF-7 modified(used in IMAP) see RFC 2060.
        /// </summary>
        private static PhpString ToUTF7Modified(Context ctx, string text)
        {
            if (string.IsNullOrEmpty(text))
                return PhpString.Empty;

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            byte[] ampSequence = new byte[] { 0x26, 0x2D }; // Means characters '&' and '-'.

            for (int i = 0; i < text.Length; i++)
            {
                // Chars from 0x20 to 0x7e are unchanged excepts "&" which is replaced by "&-".
                if (text[i] >= 0x20 && text[i] <= 0x7e)
                {
                    if (text[i] == 0x26)
                        writer.Write(ampSequence);
                    else
                        writer.Write(text[i]);
                }
                else // Collects all bytes until Char from 0x20 to 0x7e is reached.
                {
                    int start = i;
                    while (i < text.Length && (text[i] < 0x20 || text[i] > 0x7e))
                        i++;

                    string sequence = text.Substring(start, i - start);
                    // By RFC it shloud be encoded by UTF16BE, but PHP behaves in a different way.
                    byte[] sequenceEncoded = ctx.StringEncoding.GetBytes(sequence);

                    string base64Modified = System.Convert.ToBase64String(sequenceEncoded).Replace('/', ',').Trim('=');

                    writer.Write('&');
                    writer.Write(Encoding.ASCII.GetBytes(base64Modified));
                    writer.Write('-');

                    if (i < text.Length)
                        i--;
                }
            }

            writer.Flush();
            return new PhpString(stream.ToArray());
        }

        /// <summary>
        /// Converts UTF-7 modified(used in IMAP) see RFC 2060 encoding to .
        /// </summary>
        private static PhpString FromUTF7Modified(Context ctx, PhpString text)
        {
            if (text.IsEmpty)
                return string.Empty;

            byte[] utf7Modified = text.ToBytes(ctx.StringEncoding);

            using MemoryStream stream = new MemoryStream();
            using BinaryWriter writer = new BinaryWriter(stream);

            for (int i = 0; i < utf7Modified.Length; i++)
            {
                if (utf7Modified[i] == '&')
                {
                    if (i == utf7Modified.Length - 1)
                        throw new FormatException(); // Error

                    if (utf7Modified[++i] == '-') // Means "&" char.
                    {
                        writer.Write((byte)'&');
                    }
                    else // Shifting
                    {       
                        int start = i;
                        while (i < utf7Modified.Length && utf7Modified[i] != '-')
                            i++;

                        string sequence = Encoding.ASCII.GetString(utf7Modified, start, i - start).Replace(',','/');

                        if ((sequence.Length % 4) != 0) // Adds padding
                            sequence = sequence.PadRight(sequence.Length + 4 - (sequence.Length % 4),'=');

                        byte[] base64Decoded = System.Convert.FromBase64String(sequence);

                        writer.Write(base64Decoded);
                    }
                }
                else if (text[i] >= 0x20 && text[i] <= 0x7e)
                {
                    writer.Write((byte)text[i]);
                }
                else
                {
                    throw new FormatException(); // Error
                }
            }

            writer.Flush();
            return new PhpString(stream.ToArray());
        }

        /// <summary>
        /// Converts ISO-8859-1 string to modified UTF-7 text.
        /// </summary>
        /// <param name="ctx">The context of script.</param>
        /// <param name="data">An ISO-8859-1 string.</param>
        /// <returns>Returns data encoded with the modified UTF-7 encoding as defined in RFC 2060</returns>
        public static PhpString imap_utf7_encode(Context ctx, PhpString data)
        {













            return ToUTF7Modified(ctx, data.ToString(ctx));
        }

        /// <summary>
        /// Decodes modified UTF-7 text into ISO-8859-1 string.
        /// </summary>
        /// <param name="ctx"></param>
        /// <param name="text">A modified UTF-7 encoding string, as defined in RFC 2060</param>
        /// <returns>Returns a string that is encoded in ISO-8859-1 and consists of the same sequence of characters in text,
        /// or FALSE if text contains invalid modified UTF-7 sequence or
        /// text contains a character that is not part of ISO-8859-1 character set.</returns>
        public static PhpString imap_utf7_decode(Context ctx, PhpString text)
        {
            return FromUTF7Modified(ctx, text);
        }
        #endregion

        #region base64

        /// <summary>
        /// Decodes the given BASE-64 encoded text.
        /// </summary>
        /// <param name="ctx">The context of script.</param>
        /// <param name="text">The encoded text.</param>
        /// <returns>Returns the decoded message as a string.</returns>
        public static string imap_base64(Context ctx, string text)
        {
            try
            {
                return ctx.StringEncoding.GetString(Base64Utils.FromBase64(text.AsSpan(), true));
            }
            catch (FormatException)
            {

                return string.Empty;
            }    
        }

        #endregion

        #endregion

        #region connection, errors, quotas

        /// <summary>
        /// Represents parsed "conection string" from image_open.
        /// </summary>
        internal class MailBoxInfo
        {
            public string Hostname { get; set; }
            public int Port { get; set; }
            public string MailBoxName { get; set; }
            public HashSet<string> NameFlags { get; set; } = new HashSet<string>();
            public string Service { get; set; }
            public string User { get; set; }
            public string Authuser { get; set; }
        }

        /// <summary>
        /// Parses mailbox.
        /// </summary>
        /// <param name="mailbox">The mailbox has the format: "{" remote_system_name [":" port] [flags] "}" [mailbox_name]</param>
        /// <param name="info">Parsed information about mailbox.</param>
        /// <returns>True on Success, False on failure.</returns>
        static bool TryParseHostName(string mailbox, out MailBoxInfo info)
        {
            info = new MailBoxInfo();
            if (String.IsNullOrEmpty(mailbox))
                return false;

            int index = 0;
            int startSection = index;

            string GetName(string mailbox)
            {
                int startIndex = index;
                while (mailbox.Length > index && ((mailbox[index] >= 'a' && mailbox[index] <= 'z')
                  || (mailbox[index] >= 'A' && mailbox[index] <= 'Z') || (mailbox[index] >= '0' && mailbox[index] <= '9')) || mailbox[index] == '-')
                    index++;

                return (index == startIndex) ? null : mailbox.Substring(startIndex, index - startIndex);
            }

            // Mandatory char '{' 
            if (mailbox[index++] != '{') 
                return false;

            // Finds remote_system_name
            startSection = index;
            while (mailbox.Length > index && mailbox[index] != '/' && mailbox[index] != ':' && mailbox[index] != '}')
                index++;

            if (startSection == index)
                return false;
            else
                info.Hostname = mailbox.Substring(1, index - 1);

            // Finds port number
            startSection = index + 1;
            if (mailbox[index++] == ':')
            {
                while (mailbox.Length > index && mailbox[index] >= '0' && mailbox[index] <= '9')
                    index++;

                if (mailbox[index] != '/' && mailbox[index] != '}' && index == startSection)
                    return false;
                else
                    info.Port = int.Parse(mailbox.Substring(startSection, index - startSection));
            }

            // Finds flags
            startSection = index + 1;
            if (mailbox[index] == '/')
            {
                index++;
                while (true)
                {
                    string flag = GetName(mailbox);

                    if (String.IsNullOrEmpty(flag))
                        return false;

                    if (flag == "service" || flag == "user" || flag == "authuser")
                    {
                        if (mailbox[index++] != '=')
                            return false;

                        string name = GetName(mailbox);
                        if (String.IsNullOrEmpty(name))
                            return false;

                        switch (flag)
                        {
                            case "service":
                                info.Authuser = name;
                                break;
                            case "user":
                                info.User = name;
                                break;
                            case "authuser":
                                info.Authuser = name;
                                break;
                        }
                    }
                    else
                    {
                        info.NameFlags.Add(flag);
                    }

                    if (mailbox.Length <= index || mailbox[index] == '}')
                        break;
                    else
                        startSection = ++index;
                }
            }

            // Mandatory char '{' 
            if (mailbox.Length <= index || mailbox[index] != '}')
                return false;

            // Finds mailbox box directory.
            if (mailbox.Length > ++index)
                info.MailBoxName = mailbox.Substring(index, mailbox.Length - index);

            return true;
        }

        /// <summary>
        /// Open an IMAP stream to a mailbox. This function can also be used to open streams to POP3 and NNTP servers, but some functions and features are only available on IMAP servers.
        /// </summary>
        /// <param name="mailbox">A mailbox name consists of a server and a mailbox path on this server.</param>
        /// <param name="username">The user name.</param>
        /// <param name="password">The password associated with the username.</param>
        /// <param name="options">The options are a bit mask of connection options.</param>
        /// <param name="n_retries">Number of maximum connect attempts.</param>
        /// <param name="params">Connection parameters.</param>
        /// <returns>Returns an IMAP stream on success or FALSE on error.</returns>
        [return: CastToFalse]
        public static PhpResource imap_open(string mailbox, string username , string password, int options, int n_retries, PhpArray @params)
        {
            // Unsupported flags: authuser, debug, (nntp - can be done), readonly
            // Unsupported options: OP_SECURE, OP_PROTOTYPE, OP_SILENT, OP_SHORTCACHE, OP_DEBUG, OP_READONLY, OP_ANONYMOUS, OP_HALFOPEN, CL_EXPUNGE
            // Unsupported n_retries, params

            if (!TryParseHostName(mailbox, out MailBoxInfo info))
                return null;

            if (!String.IsNullOrEmpty(info.User))
                username = info.User;

            if (info.NameFlags.Contains("anonymous"))
                username = "ANONYMOUS";

            try
            {
                MailResource resource = MailResource.Create(info);
                
                if (resource == null)
                    return null;
                if (!resource.Login(username, password))
                    return null;

                if (resource is ImapResource imap) // There is only one folder in POP3.
                {
                    if (String.IsNullOrEmpty(info.MailBoxName))
                        imap.Select("INBOX");
                    else
                        imap.Select(info.MailBoxName);
                }
      
                return resource;
            }
            catch (SocketException)
            {
                return null;
            }
        }

        /// <summary>
        /// Closes the imap stream.
        /// </summary>
        /// <param name="imap_stream">An IMAP stream returned by imap_open().</param>
        /// <param name="flag">If set to CL_EXPUNGE, the function will silently expunge the mailbox before closing, removing all messages marked for deletion. You can achieve the same thing by using imap_expunge()</param>
        /// <returns>Returns TRUE on success or FALSE on failure.</returns>
        public static bool imap_close(PhpResource imap_stream, int flag = 0)
        {
            MailResource resource = ValidateMailResource(imap_stream);
            if (resource == null)
                return false;

            if ((flag & CL_EXPUNGE) == CL_EXPUNGE)
            {
                //TODO: Call imap_expunge
                throw new NotImplementedException();
            }

            resource.Close();
            return true;
        }
        #endregion
    }
}
