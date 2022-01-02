About
=====
This is a TextMate/Sublime/VS Code/Atom bundle providing language support for x86_64 assembly language in a variety of dialects (nasm/yasm primarily, but could highlight tasm/gas to some extent).

Features
========
* Basic support for NASM/YASM/TASM/GAS syntaxes
* Most up-to-date instruction sets:
  * Legacy/undocumented registers and instructions
  * FPU/MMX/3DNow!
  * SSE/SSE2/(S)SSE3/SSE4/SSE4.1/SSE4.2/SSE4.a
  * AVX/AVX2/AVX512, including pseudo-ops
  * AES-NI/SHA
  * VMX/SMX/NPX/SGX
  * other AMD/Cyrix/VIA and planned future Intel extensions

Source code
===========
VS Code package is exported from [Textmate/Sublime Bundle](https://github.com/13xforever/x86-assembly-textmate-bundle), all issues and pull requests should be filed there.

To compile the yaml sources to tmLanguage, you'll need [PackageDev](https://packagecontrol.io/packages/PackageDev). 

Contributors
============
[YASM tests](https://github.com/13xforever/x86_64-assembly-vscode/blob/master/Tests/yasm-regression) are provided by [yasm-regression](https://github.com/yasm/yasm-regression) project.

Examples
========
* Sublime Text with Monokai Extended Bright color scheme
  ![Sublime Text with Monokai Extended Bright color scheme](https://github.com/13xforever/x86-assembly-textmate-bundle/blob/master/Screenshots/Sublime%20Text%203%20-%20Monokai%20Extended%20Bright.png?raw=true)
* Atom with One Dark syntax theme
  ![Atom with One Dark syntax theme](https://github.com/13xforever/x86-assembly-textmate-bundle/blob/master/Screenshots/Atom%20-%20One%20Dark.png?raw=true)
* Visual Studio Code with default Dark color theme
  ![Visual Studio Code with default Dark color theme](https://github.com/13xforever/x86-assembly-textmate-bundle/blob/master/Screenshots/Visual%20Studio%20Code%20-%20Dark.png?raw=true)
