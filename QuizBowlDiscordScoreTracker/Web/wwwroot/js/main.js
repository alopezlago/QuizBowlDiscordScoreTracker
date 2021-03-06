"use strict";

var nameDisplay = document.getElementById("name");

function removeBarrier() {
    document.getElementById("interactBarrier").style.display = "none";
}

if (window.location.search.length <= 1) {
    nameDisplay.textContent = "No channel specified."
} else {
    var connection = new signalR.HubConnectionBuilder().withUrl("/hub").build();
    var buzzSound = document.getElementById("buzzSound");

    var shouldEmitSound = true;

    connection.on("PlayerBuzz", function (name) {
        document.body.style.backgroundColor = "red";
        nameDisplay.textContent = name;

        if (shouldEmitSound) {
            buzzSound.play();
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
}
