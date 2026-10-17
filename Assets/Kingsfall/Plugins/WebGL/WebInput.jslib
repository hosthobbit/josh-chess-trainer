// Phones only show their keyboard for real text inputs: ask for the leaderboard name with the
// browser's own prompt instead of the game's drawn name box.
mergeInto(LibraryManager.library, {
  WebPromptName: function (currentPtr) {
    var current = UTF8ToString(currentPtr);
    var name = window.prompt("Your name for the leaderboard (2 to 16 letters)", current);
    if (name === null) name = current;
    var size = lengthBytesUTF8(name) + 1;
    var buf = _malloc(size);
    stringToUTF8(name, buf, size);
    return buf;
  },
});
