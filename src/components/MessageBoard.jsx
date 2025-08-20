import React, { useState } from "react";
import { MessageSquare, Send, User, Clock } from "lucide-react";

function MessageBoard() {
  const [messages, setMessages] = useState([
    {
      id: 1,
      user: "SPT Community",
      content:
        "Welcome to SPT Launcher! This is where you'll see community messages and updates.",
      timestamp: new Date().toISOString(),
      type: "info",
    },
  ]);
  const [newMessage, setNewMessage] = useState("");

  const sendMessage = (e) => {
    e.preventDefault();
    if (!newMessage.trim()) return;

    const message = {
      id: Date.now(),
      user: "You",
      content: newMessage,
      timestamp: new Date().toISOString(),
      type: "user",
    };

    setMessages((prev) => [message, ...prev]);
    setNewMessage("");
  };

  const formatTime = (timestamp) => {
    return new Date(timestamp).toLocaleTimeString();
  };

  return (
    <div className="space-y-6">
      <div className="text-center">
        <h1 className="text-3xl font-bold text-gray-900 mb-2">
          Community Messages
        </h1>
        <p className="text-gray-600">Stay connected with the SPT community</p>
      </div>

      <div className="max-w-4xl mx-auto">
        <div className="bg-white p-6 rounded-lg border border-gray-200 shadow-sm">
          <h2 className="text-xl font-semibold mb-4 flex items-center space-x-2">
            <MessageSquare className="w-5 h-5" />
            <span>Message Board</span>
          </h2>

          <div className="space-y-4 mb-6 max-h-96 overflow-y-auto">
            {messages.map((message) => (
              <div
                key={message.id}
                className={`p-4 rounded-lg ${
                  message.type === "user"
                    ? "bg-blue-50 border border-blue-200"
                    : "bg-gray-50 border border-gray-200"
                }`}
              >
                <div className="flex items-center justify-between mb-2">
                  <div className="flex items-center space-x-2">
                    <User className="w-4 h-4" />
                    <span className="font-medium">{message.user}</span>
                  </div>
                  <div className="flex items-center space-x-1 text-sm text-gray-500">
                    <Clock className="w-3 h-3" />
                    <span>{formatTime(message.timestamp)}</span>
                  </div>
                </div>
                <p className="text-gray-900">{message.content}</p>
              </div>
            ))}
          </div>

          <form onSubmit={sendMessage} className="flex space-x-2">
            <input
              type="text"
              value={newMessage}
              onChange={(e) => setNewMessage(e.target.value)}
              placeholder="Type your message..."
              className="flex-1 px-3 py-2 border border-gray-300 rounded-md bg-white text-gray-900"
            />
            <button
              type="submit"
              className="px-4 py-2 bg-blue-600 text-white rounded-md hover:bg-blue-700 transition-colors flex items-center space-x-2"
            >
              <Send className="w-4 h-4" />
              <span>Send</span>
            </button>
          </form>
        </div>
      </div>
    </div>
  );
}

export default MessageBoard;
