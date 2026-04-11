# Espresso
Espresso heuristic logic minimizer made C++20 Windows 10 compatible - University of California, Berkeley

# This is a Windows and C++20 Compatible build of Espresso heuristic logic minimizer

Espresso is a two level logic minimizer developed in University of California, Berkeley. You are allowed to use this tool for the project.

Please download one of the executables below according to your system.

Here is a sample input file. If this file is run w/o removing the comments they will end up in the output, otherwise it will work as shown below.

.i 4        # .i specifies the number of inputs
.o 3       # .o specifies the number of outputs
.ilb Q1 Q0 D N      # This line specifies the names of the inputs in order
.ob T1 T0 OPEN   # This line specifies the names of the outputs in order
0011 ---      #The first four digits (before the space) correspond
0101 110    # to the inputs, the three after the space correspond
0110 100    # to the outputs, both in order specified above.
0001 010
0010 100
0111 ---
1001 010
1010 010
1011 ---
1100 001
1101 001
1110 001
1111 ---
.e       # Signifies the end of the file.

In the output set the -'s represent don't cares. You only need to specify those input combinations which produce a one in one of the outputs. Lines which are skipped are assumed to be zeroes on the outputs.

When running espresso a basic command line is:

espresso -o outputfilter inputfile

For example to produce a relatively human readable output here is a good output filter for the input file given earlier:

espressotest/> espresso -o eqntott temp.in

T1 = (!Q1&D) | (!Q1&Q0&N);

T0 = (Q1&!Q0&D) | (!Q0&N) | (!Q1&Q0&N);

OPEN = (Q1&Q0);

The minimized equation for each output is given.

Running espresso w/o any output filter produces a more machine readable format: [This is probably the output you'll want to pipe into your perl scripts to generate verilog.]

espressotest/> espresso temp.in
.i 4
.o 3
.ilb Q1 Q0 D N
.ob T1 T0 OPEN
.p 5
0-1- 100
101- 010
11-- 001
-0-1 010
01-1 110
.e

The first four lines are the same as the input file. The fifth line specifies how many product terms were created following the header. Left set of numbers on each of the subsequent lines specifies the inputs for a given product term. Each digit represents one of the inputs in the order specified on the ".ilb" line. "0"'s represent inverted inputs and "1"'s represent non-inverted inputs, "-" represent inputs which are not used in a given product term. I.e., in the first line "0-1-" represents (!Q1&D).

The second half of the first line represents which outputs have that product term in their sum term. IE the "100" means that only T1 will use the product term defined in the last paragraph. Looking down each column of the second half shows which product terms are summed to produce the output for that line so in this example T1 uses only the 1st and last product terms in its sum.

For further information about Espresso, please refer to https://ptolemy.berkeley.edu/projects/embedded/pubs/downloads/espresso/index.htm 
