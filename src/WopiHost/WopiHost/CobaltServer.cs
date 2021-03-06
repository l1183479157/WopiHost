﻿using System;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WopiHost
{
    public class CobaltServer
    {
        private HttpListener m_listener;
        private ConfigManager.Config config;

        public CobaltServer(ConfigManager.Config _config)
        {
            config = _config;
        }

        public void Start()
        {
            m_listener = new HttpListener();
            m_listener.Prefixes.Add(config.url);
            m_listener.Start();
            m_listener.BeginGetContext(ProcessRequest, m_listener);

            Console.WriteLine(@"WopiServer Started");
        }

        public void Stop()
        {
            m_listener.Stop();
        }

        private void ErrorResponse(HttpListenerContext context, string errmsg)
        {
            byte[] buffer = Encoding.UTF8.GetBytes(errmsg);
            context.Response.ContentLength64 = buffer.Length;
            context.Response.ContentType = @"application/json";
            context.Response.OutputStream.Write(buffer, 0, buffer.Length);
            context.Response.OutputStream.Close();
            context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
            context.Response.Close();
        }

        private void ProcessRequest(IAsyncResult result)
        {
            try
            {
                HttpListener listener = (HttpListener)result.AsyncState;
                HttpListenerContext context = listener.EndGetContext(result);
                Console.WriteLine(DateTime.Now.ToString()+" 访问地址:"+context.Request.Url);
                try
                {
                    Console.WriteLine(context.Request.HttpMethod + @" " + context.Request.Url.AbsolutePath);
                    var stringarr = context.Request.Url.AbsolutePath.Split('/');
                    var access_token = context.Request.QueryString["access_token"];

                    if (stringarr.Length < 3)
                    {
                        Console.WriteLine(@"Invalid request");
                        ErrorResponse(context, @"Invalid request parameter");
                        m_listener.BeginGetContext(ProcessRequest, m_listener);
                        return;
                    }
                    var len = stringarr.Length;
                    var filename = stringarr[3];
                    var path = context.Request.Url.AbsolutePath;
                    //取文件真实路径
                    var newpath = path.Substring(0, path.LastIndexOf("wopi/files/"));
                    //取文件名加其他参数;
                    var newpathfilename = path.Substring(path.LastIndexOf("wopi/files/") + "wopi/files/".Length, path.Length - (path.LastIndexOf("wopi/files/") + "wopi/files/".Length));
                    //分割文件名和参数
                    var newpathfilenamelist = newpathfilename.Split('/');
                    //赋值文件名
                    filename = newpathfilenamelist[0];
                    //use filename as session id just test, recommend use file id and lock id as session id
                    EditSession editSession = EditSessionManager.Instance.GetSession(filename);
                    if (editSession == null)
                    {
                        var fileExt = filename.Substring(filename.LastIndexOf('.') + 1);
                        editSession = new FileSession(filename, config.root.TrimEnd('/')+ newpath + filename, config.login, config.name, config.mail, false);
                        EditSessionManager.Instance.AddSession(editSession);
                    }

                    if (newpathfilename.IndexOf(@"/contents")==-1 && context.Request.HttpMethod.Equals(@"GET"))
                    {
                        //request of checkfileinfo, will be called first
                        var memoryStream = new MemoryStream();
                        var json = new DataContractJsonSerializer(typeof(WopiCheckFileInfo));
                        json.WriteObject(memoryStream, editSession.GetCheckFileInfo());
                        memoryStream.Flush();
                        memoryStream.Position = 0;
                        StreamReader streamReader = new StreamReader(memoryStream);
                        var jsonResponse = Encoding.UTF8.GetBytes(streamReader.ReadToEnd());

                        context.Response.ContentType = @"application/json";
                        context.Response.ContentLength64 = jsonResponse.Length;
                        context.Response.OutputStream.Write(jsonResponse, 0, jsonResponse.Length);
                        context.Response.Close();
                    }
                    else if (newpathfilename.IndexOf(@"/contents")>=0)
                    {
                        // get and put file's content
                        if (context.Request.HttpMethod.Equals(@"POST"))
                        {
                            var ms = new MemoryStream();
                            context.Request.InputStream.CopyTo(ms);
                            editSession.Save(ms.ToArray());
                            context.Response.ContentLength64 = 0;
                            context.Response.ContentType = @"text/html";
                            context.Response.StatusCode = (int)HttpStatusCode.OK;
                        }
                        else
                        {
                            var content = editSession.GetFileContent();
                            context.Response.ContentType = @"application/octet-stream";
                            context.Response.ContentLength64 = content.Length;
                            context.Response.OutputStream.Write(content, 0, content.Length);
                        }
                        context.Response.Close();
                    }
                    else if (context.Request.HttpMethod.Equals(@"POST") &&
                        (context.Request.Headers["X-WOPI-Override"].Equals("LOCK") ||
                        context.Request.Headers["X-WOPI-Override"].Equals("UNLOCK") ||
                        context.Request.Headers["X-WOPI-Override"].Equals("REFRESH_LOCK"))
                        )
                    {
                        //lock, 
                        Console.WriteLine("request lock: " + context.Request.Headers["X-WOPI-Override"]);
                        context.Response.ContentLength64 = 0;
                        context.Response.ContentType = @"text/html";
                        context.Response.StatusCode = (int)HttpStatusCode.OK;
                        context.Response.Close();
                    }
                    else
                    {
                        Console.WriteLine(@"Invalid request parameters");
                        ErrorResponse(context, @"Invalid request cobalt parameter");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(@"process request exception:" + ex.Message);
                }
                m_listener.BeginGetContext(ProcessRequest, m_listener);
            }
            catch (Exception ex)
            {
                Console.WriteLine(@"get request context:" + ex.Message);
                return;
            }
        }
    }
}
