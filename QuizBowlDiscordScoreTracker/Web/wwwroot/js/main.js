"use strict";

var connection = new signalR.HubConnectionBuilder().withUrl("/hub").build();
var nameDisplay = document.getElementById("name");
var sound1 = document.getElementById("sound1");

var shouldEmitSound = true;

function removeBarrier() {
    document.getElementById("interactBarrier").style.display = "none";
}

connection.on("PlayerBuzz", function (name) {
    document.body.style.backgroundColor = "red";
    nameDisplay.textContent = name;

    if (shouldEmitSound) {
        sound1.play();
    }

    shouldEmitSound = false;
});

connection.on("Clear", function () {
    document.body.style.backgroundColor = "green";
    nameDisplay.textContent = "";
    shouldEmitSound = true;
});

connection.start().then(function () {
    connection.invoke("AddToChannel", window.location.search.substring(1));
}).catch(function (err) {
    return console.error(err.toString());
});