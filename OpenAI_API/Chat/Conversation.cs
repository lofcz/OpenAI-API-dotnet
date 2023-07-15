﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using OpenAI_API.ChatFunctions;
using static System.Net.WebRequestMethods;

namespace OpenAI_API.Chat
{
	/// <summary>
	/// Represents on ongoing chat with back-and-forth interactions between the user and the chatbot.  This is the simplest way to interact with the ChatGPT API, rather than manually using the ChatEnpoint methods.  You do lose some flexibility though.
	/// </summary>
	public class Conversation
	{
		/// <summary>
		/// An internal reference to the API endpoint, needed for API requests
		/// </summary>
		private readonly ChatEndpoint _endpoint;

		/// <summary>
		/// Allows setting the parameters to use when calling the ChatGPT API.  Can be useful for setting temperature, presence_penalty, and more.  <see href="https://platform.openai.com/docs/api-reference/chat/create">Se  OpenAI documentation for a list of possible parameters to tweak.</see>
		/// </summary>
		public ChatRequest RequestParameters { get; private set; }

		/// <summary>
		/// Specifies the model to use for ChatGPT requests.  This is just a shorthand to access <see cref="RequestParameters"/>.Model
		/// </summary>
		public Models.Model Model
		{
			get => RequestParameters.Model;
			set => RequestParameters.Model = value;
		}

		/// <summary>
		/// If not null, this func is called after a function is resolved
		/// </summary>
		public Func<FunctionResult, ChatMessage, Task> OnAfterFunctionCall { get; set; }
		
		/// <summary>
		/// After calling <see cref="GetResponseFromChatbotAsync"/>, this contains the full response object which can contain useful metadata like token usages, <see cref="ChatChoice.FinishReason"/>, etc.  This is overwritten with every call to <see cref="GetResponseFromChatbotAsync"/> and only contains the most recent result.
		/// </summary>
		public ChatResult MostRecentApiResult { get; private set; }

		/// <summary>
		/// Creates a new conversation with ChatGPT chat
		/// </summary>
		/// <param name="endpoint">A reference to the API endpoint, needed for API requests.  Generally should be <see cref="OpenAIAPI.Chat"/>.</param>
		/// <param name="model">Optionally specify the model to use for ChatGPT requests.  If not specified, used <paramref name="defaultChatRequestArgs"/>.Model or falls back to <see cref="OpenAI_API.Models.Model.ChatGPTTurbo"/></param>
		/// <param name="defaultChatRequestArgs">Allows setting the parameters to use when calling the ChatGPT API.  Can be useful for setting temperature, presence_penalty, and more.  See <see href="https://platform.openai.com/docs/api-reference/chat/create">OpenAI documentation for a list of possible parameters to tweak.</see></param>
		public Conversation(ChatEndpoint endpoint, Models.Model model = null, ChatRequest defaultChatRequestArgs = null)
		{
			RequestParameters = new ChatRequest(defaultChatRequestArgs);
			if (model != null)
				RequestParameters.Model = model;
			RequestParameters.Model ??= Models.Model.ChatGPTTurbo;

			_messages = new List<ChatMessage>();
			_endpoint = endpoint;
			RequestParameters.NumChoicesPerMessage = 1;
			RequestParameters.Stream = false;
		}

		/// <summary>
		/// A list of messages exchanged so far.  Do not modify this list directly.  Instead, use <see cref="AppendMessage(ChatMessage)"/>, <see cref="AppendUserInput(string)"/>, <see cref="AppendSystemMessage(string)"/>, or <see cref="AppendExampleChatbotOutput(string)"/>.
		/// </summary>
		public IReadOnlyList<ChatMessage> Messages => _messages.ToList();
		private readonly List<ChatMessage> _messages;

		/// <summary>
		/// Appends a <see cref="ChatMessage"/> to the chat history
		/// </summary>
		/// <param name="message">The <see cref="ChatMessage"/> to append to the chat history</param>
		public void AppendMessage(ChatMessage message)
		{
			_messages.Add(message);
		}
		
		/// <summary>
		/// Appends a <see cref="ChatMessage"/> to the chat hstory
		/// </summary>
		/// <param name="message">The <see cref="ChatMessage"/> to append to the chat history</param>
		/// <param name="position">Zero-based index at which to insert the message</param>
		public void AppendMessage(ChatMessage message, int position)
		{
			_messages.Insert(position, message);
		}

		/// <summary>
		/// Removes given message from the conversation. If the message is not found, nothing happens
		/// </summary>
		/// <param name="message"></param>
		/// <returns>Whether message was removed</returns>
		public bool RemoveMessage(ChatMessage message)
		{
			ChatMessage msg = _messages.FirstOrDefault(x => x.Id == message.Id);
			
			if (msg != null)
			{
				_messages.Remove(msg);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Removes message with given id from the conversation. If the message is not found, nothing happens
		/// </summary>
		/// <param name="id"></param>
		/// <returns>Whether message was removed</returns>
		public bool RemoveMessage(Guid id)
		{
			ChatMessage msg = _messages.FirstOrDefault(x => x.Id == id);
			
			if (msg != null)
			{
				_messages.Remove(msg);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Updates text of a given message
		/// </summary>
		/// <param name="message">Message to update</param>
		/// <param name="content">New text</param>
		public void EditMessageContent(ChatMessage message, string content)
		{
			message.Content = content;
		}
		
		/// <summary>
		/// Finds a message in the conversation by id. If found, updates text of this message
		/// </summary>
		/// <param name="id">Message to update</param>
		/// <param name="content">New text</param>
		/// <returns>Whether message was updated</returns>
		public bool EditMessageContent(Guid id, string content)
		{
			ChatMessage msg = _messages.FirstOrDefault(x => x.Id == id);
			
			if (msg != null)
			{
				msg.Content = content;
				return true;
			}

			return false;
		}
		
		/// <summary>
		/// Updates role of a given message
		/// </summary>
		/// <param name="message">Message to update</param>
		/// <param name="role">New role</param>
		public void EditMessageRole(ChatMessage message, ChatMessageRole role)
		{
			message.Role = role;
		}
		
		/// <summary>
		/// Finds a message in the conversation by id. If found, updates text of this message
		/// </summary>
		/// <param name="id">Message to update</param>
		/// <param name="role">New role</param>
		/// <returns>Whether message was updated</returns>
		public bool EditMessageRole(Guid id, ChatMessageRole role)
		{
			ChatMessage msg = _messages.FirstOrDefault(x => x.Id == id);
			
			if (msg != null)
			{
				msg.Role = role;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history
		/// </summary>
		/// <param name="role">The <see cref="ChatMessageRole"/> for the message.  Typically, a conversation is formatted with a system message first, followed by alternating user and assistant messages.  See <see href="https://platform.openai.com/docs/guides/chat/introduction">the OpenAI docs</see> for more details about usage.</param>
		/// <param name="content">The content of the message)</param>
		public void AppendMessage(ChatMessageRole role, string content) => AppendMessage(new ChatMessage(role, content));
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory
		/// </summary>
		/// <param name="role">The <see cref="ChatMessageRole"/> for the message.  Typically, a conversation is formatted with a system message first, followed by alternating user and assistant messages.  See <see href="https://platform.openai.com/docs/guides/chat/introduction">the OpenAI docs</see> for more details about usage.</param>
		/// <param name="content">The content of the message</param>
		/// <param name="id">Id of the message</param>
		public void AppendMessage(ChatMessageRole role, string content, Guid? id) => AppendMessage(new ChatMessage(role, content, id));

		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history with the Role of <see cref="ChatMessageRole.User"/>.  The user messages help instruct the assistant. They can be generated by the end users of an application, or set by a developer as an instruction.
		/// </summary>
		/// <param name="content">Text content generated by the end users of an application, or set by a developer as an instruction</param>
		public void AppendUserInput(string content) => AppendMessage(new ChatMessage(ChatMessageRole.User, content));
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory with the Role of <see cref="ChatMessageRole.User"/>.  The user messages help instruct the assistant. They can be generated by the end users of an application, or set by a developer as an instruction.
		/// </summary>
		/// <param name="content">Text content generated by the end users of an application, or set by a developer as an instruction</param>
		/// <param name="id">id of the message</param>
		public void AppendUserInput(string content, Guid id) => AppendMessage(new ChatMessage(ChatMessageRole.User, content, id));

		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history with the Role of <see cref="ChatMessageRole.User"/>.  The user messages help instruct the assistant. They can be generated by the end users of an application, or set by a developer as an instruction.
		/// </summary>
		/// <param name="userName">The name of the user in a multi-user chat</param>
		/// <param name="content">Text content generated by the end users of an application, or set by a developer as an instruction</param>
		public void AppendUserInputWithName(string userName, string content) => AppendMessage(new ChatMessage(ChatMessageRole.User, content) { Name = userName });
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory with the Role of <see cref="ChatMessageRole.User"/>.  The user messages help instruct the assistant. They can be generated by the end users of an application, or set by a developer as an instruction.
		/// </summary>
		/// <param name="userName">The name of the user in a multi-user chat</param>
		/// <param name="content">Text content generated by the end users of an application, or set by a developer as an instruction</param>
		/// <param name="id">id of the message</param>
		public void AppendUserInputWithName(string userName, string content, Guid id) => AppendMessage(new ChatMessage(ChatMessageRole.User, content, id) { Name = userName });
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history with the Role of <see cref="ChatMessageRole.System"/>.  The system message helps set the behavior of the assistant.
		/// </summary>
		/// <param name="content">text content that helps set the behavior of the assistant</param>
		public void AppendSystemMessage(string content) => AppendMessage(new ChatMessage(ChatMessageRole.System, content));
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory with the Role of <see cref="ChatMessageRole.System"/>.  The system message helps set the behavior of the assistant.
		/// </summary>
		/// <param name="content">text content that helps set the behavior of the assistant</param>
		/// <param name="id">id of the message</param>
		public void AppendSystemMessage(string content, Guid id) => AppendMessage(new ChatMessage(ChatMessageRole.System, content, id));
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory with the Role of <see cref="ChatMessageRole.System"/>.  The system message helps set the behavior of the assistant.
		/// </summary>
		/// <param name="content">text content that helps set the behavior of the assistant</param>
		/// <param name="id">id of the message</param>
		public void PrependSystemMessage(string content, Guid id) => AppendMessage(new ChatMessage(ChatMessageRole.System, content, id), 0);
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history with the Role of <see cref="ChatMessageRole.Assistant"/>.  Assistant messages can be written by a developer to help give examples of desired behavior.
		/// </summary>
		/// <param name="content">Text content written by a developer to help give examples of desired behavior</param>
		public void AppendExampleChatbotOutput(string content) => AppendMessage(new ChatMessage(ChatMessageRole.Assistant, content));
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat history with the Role of <see cref="ChatMessageRole.Function"/>.  The function message is a response to a request from the system for output from a predefined function.
		/// </summary>
		/// <param name="functionName">The name of the function for which the content has been generated as the result</param>
		/// <param name="content">The text content (usually JSON)</param>
		public void AppendFunctionMessage(string functionName, string content) => AppendMessage(new ChatMessage(ChatMessageRole.Function, content) { Name = functionName });
		
		/// <summary>
		/// Creates and appends a <see cref="ChatMessage"/> to the chat hstory with the Role of <see cref="ChatMessageRole.Assistant"/>.  Assistant messages can be written by a developer to help give examples of desired behavior.
		/// </summary>
		/// <param name="content">Text content written by a developer to help give examples of desired behavior</param>
		/// <param name="id">id of the message</param>
		public void AppendExampleChatbotOutput(string content, Guid id) => AppendMessage(new ChatMessage(ChatMessageRole.Assistant, content, id));

		#region Non-streaming

		/// <summary>
		/// Calls the API to get a response, which is appended to the current chat's <see cref="Messages"/> as an <see cref="ChatMessageRole.Assistant"/> <see cref="ChatMessage"/>.
		/// </summary>
		/// <returns>The string of the response from the chatbot API</returns>
		public async Task<string> GetResponseFromChatbotAsync()
		{
			ChatRequest req = new ChatRequest(RequestParameters)
			{
				Messages = _messages.ToList()
			};

			ChatResult res = await _endpoint.CreateChatCompletionAsync(req);
			MostRecentApiResult = res;

			if (res.Choices.Count > 0)
			{
				ChatMessage newMsg = res.Choices[0].Message;
				AppendMessage(newMsg);
				return newMsg.Content;
			}
			return null;
		}

		/// <summary>
		/// OBSOLETE: GetResponseFromChatbot() has been renamed to <see cref="GetResponseFromChatbotAsync"/> to follow .NET naming guidelines.  This alias will be removed in a future version.
		/// </summary>
		/// <returns>The string of the response from the chatbot API</returns>
		[Obsolete("Conversation.GetResponseFromChatbot() has been renamed to GetResponseFromChatbotAsync to follow .NET naming guidelines.  Please update any references to GetResponseFromChatbotAsync().  This alias will be removed in a future version.", false)]
		public Task<string> GetResponseFromChatbot() => GetResponseFromChatbotAsync();


		#endregion

		#region Streaming

		/// <summary>
		/// Calls the API to get a response, which is appended to the current chat's <see cref="Messages"/> as an <see cref="ChatMessageRole.Assistant"/> <see cref="ChatMessage"/>, and streams the results to the <paramref name="resultHandler"/> as they come in. <br/>
		/// If you are on the latest C# supporting async enumerables, you may prefer the cleaner syntax of <see cref="StreamResponseEnumerableFromChatbotAsync"/> instead.
		///  </summary>
		/// <param name="resultHandler">An action to be called as each new result arrives.</param>
		public async Task StreamResponseFromChatbotAsync(Action<string> resultHandler)
		{
			await foreach (string res in StreamResponseEnumerableFromChatbotAsync())
			{
				resultHandler(res);
			}
		}

		/// <summary>
		/// Calls the API to get a response, which is appended to the current chat's <see cref="Messages"/> as an <see cref="ChatMessageRole.Assistant"/> <see cref="ChatMessage"/>, and streams the results to the <paramref name="resultHandler"/> as they come in. <br/>
		/// If you are on the latest C# supporting async enumerables, you may prefer the cleaner syntax of <see cref="StreamResponseEnumerableFromChatbotAsync"/> instead.
		///  </summary>
		/// <param name="resultHandler">An action to be called as each new result arrives, which includes the index of the result in the overall result set.</param>
		public async Task StreamResponseFromChatbotAsync(Action<int, string> resultHandler)
		{
			int index = 0;
			await foreach (string res in StreamResponseEnumerableFromChatbotAsync())
			{
				resultHandler(index++, res);
			}
		}

		/// <summary>
		/// Calls the API to get a response, which is appended to the current chat's <see cref="Messages"/> as an <see cref="ChatMessageRole.Assistant"/> <see cref="ChatMessage"/>, and streams the results as they come in. <br/>
		/// If you are not using C# 8 supporting async enumerables or if you are using the .NET Framework, you may need to use <see cref="StreamResponseFromChatbotAsync(Action{string})"/> instead.
		/// </summary>
		/// <returns>An async enumerable with each of the results as they come in.  See <see href="https://docs.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-8#asynchronous-streams"/> for more details on how to consume an async enumerable.</returns>
		public async IAsyncEnumerable<string> StreamResponseEnumerableFromChatbotAsync(Guid? messageId = null)
		{
			ChatRequest req = new ChatRequest(RequestParameters)
			{
				Messages = _messages.ToList()
			};

			StringBuilder responseStringBuilder = new StringBuilder();
			ChatMessageRole responseRole = null;

			await foreach (ChatResult res in _endpoint.StreamChatEnumerableAsync(req))
			{
				
				
				if (res.Choices.FirstOrDefault()?.Delta is { } delta)
				{
					if (responseRole == null && delta.Role != null)
					{
						responseRole = delta.Role;
					}

					string deltaContent = delta.Content;

					if (!string.IsNullOrEmpty(deltaContent))
					{
						responseStringBuilder.Append(deltaContent);
						yield return deltaContent;
					}
				}
				MostRecentApiResult = res;
			}

			if (responseRole != null)
			{
				AppendMessage(responseRole, responseStringBuilder.ToString(), messageId);
			}
		}
		
		/// <summary>
		/// Stream LLM response. If the response is of type "function", entire response is buffered and then <see cref="functionCallHandler"/> is invoked.
		/// Otherwise the message is intended for the end user and <see cref="messageTokenHandler"/> is called for each incoming token
		/// </summary>
		/// <param name="messageId"></param>
		/// <param name="messageTokenHandler"></param>
		/// <param name="functionCallHandler"></param>
		/// <param name="messageTypeResolvedHandler">This is called typically after the first token arrives signaling type of the incoming message</param>
		public async Task StreamResponseEnumerableFromChatbotAsyncWithFunctions(Guid? messageId, Func<string, Task> messageTokenHandler, Func<List<FunctionCall>, Task<FunctionResult?>> functionCallHandler, Func<ChatMessageRole, Task> messageTypeResolvedHandler)
		{
			ChatRequest req = new ChatRequest(RequestParameters)
			{
				Messages = _messages.ToList()
			};

			StringBuilder responseStringBuilder = new StringBuilder();
			ChatMessageRole responseRole = null;
			string currentFunction = "";
			Dictionary<string, StringBuilder> functionCalls = new Dictionary<string, StringBuilder>();
			bool typeResolved = false;

			await foreach (ChatResult res in _endpoint.StreamChatEnumerableAsync(req))
			{
				if (!res.Choices.Any())
				{
					MostRecentApiResult = res;
					continue;
				}
				
				ChatChoice choice = res.Choices[0];
                
				if (choice.Delta != null)
				{
					ChatMessage delta = choice.Delta;
					string deltaContent = delta.Content;
					string finishReason = choice.FinishReason;
					bool empty = string.IsNullOrEmpty(deltaContent);

					if (responseRole == null && finishReason == "function_call")
					{
						responseRole = ChatMessageRole.Function;
					}

					if (choice.Delta.FunctionCall != null)
					{
						responseRole ??= ChatMessageRole.Function;
						
						if (!typeResolved)
						{
							typeResolved = true;
							
							if (messageTypeResolvedHandler != null)
							{
								await messageTypeResolvedHandler(ChatMessageRole.Function);	
							}
						}
						
						if (choice.Delta.FunctionCall.Name != null)
						{
							currentFunction = choice.Delta.FunctionCall.Name;
							functionCalls.Add(currentFunction, new StringBuilder());
						}
						else
						{
							functionCalls[currentFunction].Append(choice.Delta.FunctionCall.Arguments);
						}
					}

					if (responseRole == null && delta.Role != null)
					{
						responseRole = delta.Role;

						if (!typeResolved)
						{
							typeResolved = true;
							
							if (messageTypeResolvedHandler != null)
							{
								await messageTypeResolvedHandler(responseRole);	
							}
						}

						if (functionCallHandler != null && responseRole == "function")
						{
							if (!empty)
							{
								responseStringBuilder.Append(deltaContent);
							}
							
							continue;
						}
					}
                    
					if (!empty)
					{
						responseStringBuilder.Append(deltaContent);

						if (messageTokenHandler != null)
						{
							await messageTokenHandler.Invoke(deltaContent);	
						}
					}
				}
				
				MostRecentApiResult = res;
			}

			if (responseRole != null && responseRole.Equals(ChatMessageRole.Function))
			{
				if (functionCallHandler != null)
				{
					List<FunctionCall> calls = functionCalls.Select(pair => new FunctionCall {  Name = pair.Key, Arguments = pair.Value.ToString() }).ToList();
					FunctionResult? fr = await functionCallHandler.Invoke(calls);

					if (fr != null)
					{
						ChatMessage msg = new ChatMessage(responseRole, fr.Content, Guid.NewGuid()) { Name = fr.Name };
						AppendMessage(msg);

						if (OnAfterFunctionCall != null)
						{
							await OnAfterFunctionCall(fr, msg);
						}	
					}
					
					return;
				}
			}

			if (responseRole != null)
			{
				AppendMessage(responseRole, responseStringBuilder.ToString(), messageId);
			}
		}
		
		#endregion
	}
}
