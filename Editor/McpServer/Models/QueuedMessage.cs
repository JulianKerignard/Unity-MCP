namespace McpUnity.Models
{
    /// <summary>
    /// Represents a message queued for processing on the main thread
    /// Used for thread-safe communication between WebSocket and Unity main thread
    /// </summary>
    public class QueuedMessage
    {
        /// <summary>
        /// The JSON-RPC message content
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Reference to the WebSocket behavior that sent this message
        /// Used to send the response back to the correct client
        /// </summary>
        public object Sender { get; set; }

        /// <summary>
        /// Default constructor
        /// </summary>
        public QueuedMessage() { }

        /// <summary>
        /// Parameterized constructor
        /// </summary>
        public QueuedMessage(string message, object sender)
        {
            Message = message;
            Sender = sender;
        }
    }
}
