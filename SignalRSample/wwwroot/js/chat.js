"use strict";


//Disable the send button until connection is established.
document.getElementById("sendButton").disabled = true;
document.getElementById("privateSendButton").disabled = true;


var Im = {}

function initSignalR() {
    var connection = new signalR.HubConnectionBuilder().withUrl("/chatHub", { accessTokenFactory: () => localStorage.getItem('chatAuthToken') }).withAutomaticReconnect().build();
     
 
    connection.on("ReceiveMessage", function (user, message) {
        var li = document.createElement("li");
        document.getElementById("messagesList").appendChild(li);
        // We can assign user-supplied strings to an element's textContent because it
        // is not interpreted as markup. If you're assigning in any other way, you 
        // should be aware of possible script injection concerns.
        li.textContent = `${user} says ${message}`;
    });
 
    connection.onclose(e => {
        console.log('ad',e)
        var li = document.createElement("li");
        document.getElementById("messagesList").appendChild(li);
        // We can assign user-supplied strings to an element's textContent because it
        // is not interpreted as markup. If you're assigning in any other way, you 
        // should be aware of possible script injection concerns.
        li.textContent = `链接已断开， ${e}`;
    })
    connection.onreconnecting(e => {
        console.log(e)
        var li = document.createElement("li");
        document.getElementById("messagesList").appendChild(li);
        // We can assign user-supplied strings to an element's textContent because it
        // is not interpreted as markup. If you're assigning in any other way, you 
        // should be aware of possible script injection concerns.
        li.textContent = `正在重连， ${e}`;
    })
    connection.onreconnected(e => {
        console.log(e)
        var li = document.createElement("li");
        document.getElementById("messagesList").appendChild(li);
        // We can assign user-supplied strings to an element's textContent because it
        // is not interpreted as markup. If you're assigning in any other way, you 
        // should be aware of possible script injection concerns.
        li.textContent = `重连成功， ${e}`;
    })
    connection.start().then(function () {
        console.log('连接成功')
        document.getElementById("sendButton").disabled = false;
        document.getElementById("privateSendButton").disabled = false;
    }).catch(function (err) {
        return console.error(err.toString());
    });
    Im.conn=connection
}
document.getElementById("sendButton").addEventListener("click", function (event) {
    var user = document.getElementById("userInput").value;
    var message = document.getElementById("messageInput").value;
    Im.conn.invoke("SendMessage", user, message).catch(function (err) {
        return console.error(err.toString());
    });
    event.preventDefault();
});
document.getElementById("privateSendButton").addEventListener("click", function (event) {
    var user = document.getElementById("userInput").value;
    var message = document.getElementById("messageInput").value;
    Im.conn.invoke("SendMessageToUser", "张无忌", message).catch(function (err) {
        return console.error(err.toString());
    });
    event.preventDefault();
});
document.getElementById("loginButton").addEventListener("click", function () {
    var user = document.getElementById("userInput").value;
    $.post("/api/values/login?user=" + user, {}).then(res => {
        localStorage.setItem("chatAuthToken", res)
        initSignalR()
    })
})