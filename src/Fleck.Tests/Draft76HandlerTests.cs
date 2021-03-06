using System;
using Fleck.Handlers;
using System.Text;
using NUnit.Framework;

namespace Fleck.Tests
{
    [TestFixtureAttribute]
    public class Draft76HandlerTests
    {

        private IHandler _handler;
        private WebSocketHttpRequest _request;
        private Action<string> _onMessage;

        [SetUp]
        public void Setup()
        {
            _request = new WebSocketHttpRequest();
            _onMessage = delegate { };

            _handler = Draft76Handler.Create(_request, s => _onMessage(s));
        }

        private const string ExampleRequest =
@"GET /demo HTTP/1.1
Host: example.com
Connection: Upgrade
Sec-WebSocket-Key2: 12998 5 Y3 1  .P00
Sec-WebSocket-Protocol: sample
Upgrade: WebSocket
Sec-WebSocket-Key1: 4 @1  46546xW%0l 1 5
Origin: http://example.com

^n:ds[4U";

        private const string ExampleResponse =
@"HTTP/1.1 101 WebSocket Protocol Handshake
Upgrade: WebSocket
Connection: Upgrade
Sec-WebSocket-Origin: http://example.com
Sec-WebSocket-Location: ws://example.com/demo
Sec-WebSocket-Protocol: sample

8jKS'y:G*Co,Wxa-";

        const string Key1 = "4 @1  46546xW%0l 1 5";
        const string Key2 = "12998 5 Y3 1  .P00";
        const string Challenge = "^n:ds[4U";
        const string ExpectedAnswer = "8jKS'y:G*Co,Wxa-";

        [Test]
        public void ShouldGenerateServerHandshake()
        {
            _request.Headers["Sec-WebSocket-Key1"] = Key1;
            _request.Headers["Sec-WebSocket-Key2"] = Key2;
            _request.Headers["Host"] = "example.com";
            _request.Headers["Connection"] = "Upgrade";
            _request.Headers["Sec-WebSocket-Protocol"] = "sample";
            _request.Headers["Origin"] = "http://example.com";
            _request.Body = Challenge;
            _request.Scheme = "ws";
            _request.Path = "/demo";
            _request.Bytes = Encoding.UTF8.GetBytes(ExampleRequest);

            var responseBytes = _handler.CreateHandshake();

            var response = Encoding.ASCII.GetString(responseBytes);

            Assert.AreEqual(ExampleResponse, response);

        }

        [Test]
        public void ShouldFrameText()
        {
            //StartByte "Hello" EndByte
            var expected = new byte[]{ 0, 72, 101, 108, 108, 111, 255 };

            var result = _handler.FrameText("Hello");

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldCallOnMessageOnCompleteFrame()
        {
            const string expected = "Once upon a time...";
            var bytes = new System.Collections.Generic.List<byte>();
            bytes.Add(0);
            bytes.AddRange(Encoding.UTF8.GetBytes(expected));
            bytes.Add(255);

            string result = null;
            _onMessage = s => result = s;

            _handler.Recieve(bytes);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldNotCallOnMessageOnIncompleteFrame()
        {
            const string expected = "Once up";
            var bytes = new System.Collections.Generic.List<byte>();
            bytes.Add(0);
            bytes.AddRange(Encoding.UTF8.GetBytes(expected));

            bool hit = false;
            _onMessage = s => hit = true;

            _handler.Recieve(bytes);
            Assert.IsFalse(hit);
        }

        [Test]
        public void ShouldCallOnMessageAfterSplitFrame()
        {
            const string part1 = "Writing tests";
            const string part2 = " is good for your health";
            const string expected = part1 + part2;

            var bytes = new System.Collections.Generic.List<byte>();
            bytes.Add(0);
            bytes.AddRange(Encoding.UTF8.GetBytes(part1));

            var bytes2 = new System.Collections.Generic.List<byte>();
            bytes.AddRange(Encoding.UTF8.GetBytes(part2));
            bytes.Add(255);

            string result = null;
            _onMessage = s => result = s;

            _handler.Recieve(bytes);
            _handler.Recieve(bytes2);
            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldThrowOnInvalidFirstFrame()
        {
            Assert.Catch<WebSocketException>(() =>_handler.Recieve(new byte[] {87}));
        }
    }
}

