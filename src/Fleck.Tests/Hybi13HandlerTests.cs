﻿using System;
using System.Text;
using Fleck.Handlers;
using NUnit.Framework;

namespace Fleck.Tests
{
    [TestFixture]
    public class Hybi13HandlerTests
    {
        private IHandler _handler;
        private WebSocketHttpRequest _request;
        private Action<string> _onMessage;
        private Action _onClose;

        [SetUp]
        public void Setup()
        {
            _request = new WebSocketHttpRequest();
            _onClose = delegate { };
            _onMessage = delegate { };

            _handler = Hybi13Handler.Create(_request, s => _onMessage(s), () => _onClose());
        }

        [Test]
        public void ShouldCreateAnswerGuid()
        {
            const string exampleRequestKey = "dGhlIHNhbXBsZSBub25jZQ==";
            const string expectedResult = "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=";

            var actual = Hybi13Handler.CreateResponseKey(exampleRequestKey);

            Assert.AreEqual(expectedResult, actual);
        }

        [Test]
        public void ShouldRespondToCompleteRequestCorrectly()
        {
            _request.Method = "GET";
            _request.Path = "/chat";
            _request.Body = "";
            _request.Headers["Host"] = "server.example.com";
            _request.Headers["Upgrade"] = "websocket";
            _request.Headers["Connection"] = "Upgrade";
            _request.Headers["Sec-WebSocket-Key"] = "dGhlIHNhbXBsZSBub25jZQ==";
            _request.Headers["Origin"] = "http://example.com";
            _request.Headers["Sec-WebSocket-Protocol"] = "chat, superchat";
            _request.Headers["Sec-WebSocket-Version"] = "13";
            _request.Bytes = Encoding.ASCII.GetBytes(ExampleRequest);

            var result = _handler.CreateHandshake();

            Assert.AreEqual(ExampleResponse, Encoding.ASCII.GetString(result));
        }

        [Test]
        public void ShouldFrameText()
        {
            //FIN:1 Type:1 (text) LEN:5 Payload:"Hello"
            var expected = new byte[]{ 129, 5, 72, 101, 108, 108, 111 };

            var result = _handler.FrameText("Hello");

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldFrameClose()
        {
            //FIN:1 Type:5 (text) LEN:2 Payload:"Hello"
            var expected = new byte[]{ 136, 2, 3, 234};

            var result = _handler.FrameClose(1002);

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldThrowWhenDataNotMasked()
        {
            //FIN:1 Type:1 (text) LEN:5 Payload:"Hello"
            var data = new byte[]{ 129, 5, 72, 101, 108, 108, 111 };

            Assert.Catch<WebSocketException>(() => _handler.Recieve(data));
        }


        [Test]
        public void ShouldCallOnMessageWhenRecievingTextFrame()
        {
            const string expected = "This be a test";
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Text,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 34298,
                    Payload = Encoding.UTF8.GetBytes(expected)
                };

            string result = null;
            _onMessage = s => result = s;
            _handler.Recieve(frame.ToBytes());

            Assert.AreEqual(expected, result);
        }


        [Test]
        public void ShouldNotCallOnMessageOnNonFinalFrame()
        {
            const string expected = "Blah blah";
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Text,
                    IsFinal = false,
                    IsMasked = true,
                    MaskKey = 34298,
                    Payload = Encoding.UTF8.GetBytes(expected)
                };

            var hit = false;
            _onMessage = s => hit = true;
            _handler.Recieve(frame.ToBytes());

            Assert.IsFalse(hit);
        }

        [Test]
        public void ShouldCallOnMessageFromSplitFrame()
        {
            const string firstPart = "Blah blah";
            const string secondPart = "Do Data";
            const string expected = firstPart + secondPart;

            var firstFrame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Text,
                    IsFinal = false,
                    IsMasked = true,
                    MaskKey = 342808,
                    Payload = Encoding.UTF8.GetBytes(firstPart)
                };

            var secondFrame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Continuation,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 345808,
                    Payload = Encoding.UTF8.GetBytes(secondPart)
                };

            string result = null;
            _onMessage = s => result = s;
            _handler.Recieve(firstFrame.ToBytes());
            _handler.Recieve(secondFrame.ToBytes());

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldThrowWhenRecievingUnexpectedContinuation()
        {
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Continuation,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 345808,
                    Payload = Encoding.UTF8.GetBytes("continue")
                };

            Assert.Catch<WebSocketException>(() => _handler.Recieve(frame.ToBytes()));
        }

        [Test]
        public void ShouldCloseOnCloseFromTextFrame()
        {
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Close,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 5232,
                    Payload = 1000.ToBigEndianBytes<ushort>()
                };

            var hit = false;
            _onClose = () => hit = true;
            _handler.Recieve(frame.ToBytes());

            Assert.IsTrue(hit);
        }

        [Test]
        public void ShouldCallOnMessageWhenRecievingTextFrameOver125Bytes()
        {
            var expected = new string('z', 8000);
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Text,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 34298,
                    Payload = Encoding.UTF8.GetBytes(expected)
                };

            string result = null;
            _onMessage = s => result = s;
            _handler.Recieve(frame.ToBytes());

            Assert.AreEqual(expected, result);
        }

        [Test]
        public void ShouldCallOnMessageWhenRecievingTextFrameLargerThanUInt16()
        {
            var expected = new string('x', UInt16.MaxValue) + new string('x', UInt16.MaxValue);
            var frame = new Hybi14DataFrame
                {
                    FrameType = FrameType.Text,
                    IsFinal = true,
                    IsMasked = true,
                    MaskKey = 34298,
                    Payload = Encoding.UTF8.GetBytes(expected)
                };

            string result = null;
            _onMessage = s => result = s;
            _handler.Recieve(frame.ToBytes());

            Assert.AreEqual(expected, result);
        }

        private const string ExampleRequest =
@"GET /chat HTTP/1.1
Host: server.example.com
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Key: dGhlIHNhbXBsZSBub25jZQ==
Origin: http://example.com
Sec-WebSocket-Protocol: chat, superchat
Sec-WebSocket-Version: 13

";

        private const string ExampleResponse =
@"HTTP/1.1 101 Switching Protocols
Upgrade: websocket
Connection: Upgrade
Sec-WebSocket-Accept: s3pPLMBiTxaQ9kYGzzhZRbK+xOo=

";
    }
}