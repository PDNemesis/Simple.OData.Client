﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Simple.OData.Client.TestUtils
{
    [DataContract]
    public class SerializableHttpRequestMessage
    {
        [DataMember]
        public string Method { get; set; }
        [DataMember]
        public Uri RequestUri { get; set; }
        [DataMember]
        public Dictionary<string, List<string>> RequestHeaders;
        [DataMember]
        public Dictionary<string, List<string>> ContentHeaders;
        [DataMember]
        public string Content { get; set; }

        public SerializableHttpRequestMessage()
        {
        }

        public SerializableHttpRequestMessage(HttpRequestMessage request)
        {
            this.Method = request.Method.ToString();
            this.RequestUri = request.RequestUri;
            this.RequestHeaders = request.Headers.Select(
                x => new KeyValuePair<string, List<string>>(x.Key, new List<string>(x.Value))).ToList()
                .ToDictionary(x => x.Key, x => x.Value);
            if (request.Content != null)
            {
                this.ContentHeaders = request.Content.Headers.Select(
                    x => new KeyValuePair<string, List<string>>(x.Key, new List<string>(x.Value))).ToList()
                    .ToDictionary(x => x.Key, x => x.Value);
                this.Content = request.Content.ReadAsStringAsync().Result;
            }
        }
    }

    [DataContract]
    public class SerializableHttpResponseMessage
    {
        [DataMember]
        public HttpStatusCode StatusCode { get; set; }
        [DataMember]
        public Uri RequestUri { get; set; }
        [DataMember]
        public Dictionary<string, List<string>> ResponseHeaders;
        [DataMember]
        public Dictionary<string, List<string>> ContentHeaders;
        [DataMember]
        public string Content { get; set; }

        public SerializableHttpResponseMessage()
        {
            
        }

        public SerializableHttpResponseMessage(HttpResponseMessage response)
        {
            this.StatusCode = response.StatusCode;
            this.RequestUri = response.RequestMessage.RequestUri;
            this.ResponseHeaders = response.Headers.Select(
                    x => new KeyValuePair<string, List<string>>(x.Key, new List<string>(x.Value))).ToList()
                .ToDictionary(x => x.Key, x => x.Value);
            if (response.Content != null)
            {
                this.ContentHeaders = response.Content.Headers.Select(
                        x => new KeyValuePair<string, List<string>>(x.Key, new List<string>(x.Value))).ToList()
                    .ToDictionary(x => x.Key, x => x.Value);
                this.Content = response.Content.ReadAsStringAsync().Result;
            }
        }
    }

    public class MockingRequestExecutor
    {
        private readonly ODataClientSettings _settings;
        private readonly string _mockDataPathBase;
        private readonly bool _recording;
        private int _fileCounter;

        public MockingRequestExecutor(ODataClientSettings settings, string mockDataPathBase, bool recording = false)
        {
            _settings = settings;
            _mockDataPathBase = mockDataPathBase;
            _recording = recording;
        }

        public async Task<HttpResponseMessage> ExecuteRequestAsync(HttpRequestMessage request)
        {
            if (_recording)
            {
                if (!IsMetadataRequest(request))
                    SaveRequest(request);

                var httpConnection = new HttpConnection(_settings);
                var response = await httpConnection.HttpClient.SendAsync(request);

                if (!IsMetadataRequest(request))
                    SaveResponse(response);
                return response;
            }
            else
            {
                await ValidateRequestAsync(request);
                return GetMockResponse(request);
            }
        }

        private bool IsMetadataRequest(HttpRequestMessage request)
        {
            return request.RequestUri.LocalPath.EndsWith(ODataLiteral.Metadata);
        }

        private string GenerateMockDataPath()
        {
            return string.Format($"{_mockDataPathBase}.{++_fileCounter}.txt");
        }

        private void SaveRequest(HttpRequestMessage request)
        {
            using (var stream = new FileStream(GenerateMockDataPath(), FileMode.Create))
            {
                var ser = new DataContractJsonSerializer(typeof(SerializableHttpRequestMessage));
                ser.WriteObject(stream, new SerializableHttpRequestMessage(request));
            }                
        }

        private void SaveResponse(HttpResponseMessage response)
        {
            using (var stream = new FileStream(GenerateMockDataPath(), FileMode.Create))
            {
                var ser = new DataContractJsonSerializer(typeof(SerializableHttpResponseMessage));
                ser.WriteObject(stream, new SerializableHttpResponseMessage(response));
            }                
        }

        private async Task ValidateRequestAsync(HttpRequestMessage request)
        {
            using (var stream = new FileStream(GenerateMockDataPath(), FileMode.Open))
            {
                var ser = new DataContractJsonSerializer(typeof(SerializableHttpRequestMessage));
                var savedRequest = ser.ReadObject(stream) as SerializableHttpRequestMessage;
                Assert.Equal(savedRequest.Method, request.Method.ToString());
                Assert.Equal(savedRequest.RequestUri.AbsolutePath.Split('/').Last(), request.RequestUri.AbsolutePath.Split('/').Last());
                var expectedHeaders = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in savedRequest.RequestHeaders)
                    expectedHeaders.Add(header.Key, header.Value);
                var actualHeaders = new Dictionary<string, IEnumerable<string>>();
                foreach (var header in request.Headers)
                    actualHeaders.Add(header.Key, header.Value);
                ValidateHeaders(expectedHeaders, actualHeaders);
                if (request.Content != null)
                {
                    expectedHeaders = new Dictionary<string, IEnumerable<string>>();
                    foreach (var header in savedRequest.ContentHeaders)
                        expectedHeaders.Add(header.Key, header.Value);
                    actualHeaders = new Dictionary<string, IEnumerable<string>>();
                    foreach (var header in request.Content.Headers)
                        actualHeaders.Add(header.Key, header.Value);
                    ValidateHeaders(expectedHeaders, actualHeaders);
                    var expectedContent = savedRequest.Content;
                    expectedContent = RemoveElements(expectedContent, new[] { "updated" });
                    var actualContent = RemoveElements(await request.Content.ReadAsStringAsync(), new[] { "updated" });
                    Assert.Equal(expectedContent, actualContent);
                }
            }
        }

        private HttpResponseMessage GetMockResponse(HttpRequestMessage request)
        {
            using (var stream = new FileStream(GenerateMockDataPath(), FileMode.Open))
            {
                var ser = new DataContractJsonSerializer(typeof(SerializableHttpResponseMessage));
                var savedResponse = ser.ReadObject(stream) as SerializableHttpResponseMessage;
                var response = new HttpResponseMessage
                {
                    StatusCode = savedResponse.StatusCode,
                    Content = savedResponse.Content == null
                        ? null
                        : new StreamContent(Utils.StringToStream(savedResponse.Content)),
                    RequestMessage = request,
                    Version = new Version(1, 1),
                };
                foreach (var header in savedResponse.ResponseHeaders)
                {
                    if (response.Headers.Contains(header.Key))
                        response.Headers.Remove(header.Key);
                    response.Headers.Add(header.Key, header.Value);
                }

                if (savedResponse.Content != null)
                {
                    foreach (var header in savedResponse.ContentHeaders)
                    {
                        if (response.Content.Headers.Contains(header.Key))
                            response.Content.Headers.Remove(header.Key);
                        response.Content.Headers.Add(header.Key, header.Value);
                    }
                }
                return response;            }
        }

        private void ValidateHeaders(
            IDictionary<string, IEnumerable<string>> expectedHeaders,
            IDictionary<string, IEnumerable<string>> actualHeaders)
        {
            Assert.Equal(expectedHeaders.Count(), actualHeaders.Count());
            foreach (var header in expectedHeaders)
            {
                Assert.Contains(header.Key, actualHeaders.Keys);
                Assert.Equal(header.Value.FirstOrDefault(), actualHeaders[header.Key].FirstOrDefault());
            }
        }

        private string RemoveElements(string content, IEnumerable<string> elementNames)
        {
            foreach (var elementName in elementNames)
            {
                while (true)
                {
                    var startPos = content.IndexOf($"<{elementName}>");
                    var endPos = content.IndexOf($"</{elementName}>");
                    if (startPos >= 0 && endPos > startPos)
                    {
                        content = content.Substring(0, startPos) + content.Substring(endPos + elementName.Length + 3);
                    }
                    else
                    {
                        break;
                    }
                }
            }
            return content;
        }
    }
}