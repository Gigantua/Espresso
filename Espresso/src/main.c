#include "espresso.h"
#include "main.h"
#include "selftest.h"
#include <string.h>
#include <stdio.h>

int opterr = 1, optind = 1, optopt, optreset;
char *optarg;

#define BADCH   (int)'?'
#define BADARG  (int)':'
#define EMSG    ""

static FILE *last_fp;
static int input_type = FD_type;

void getPLA(int opt, int argc, char **argv, int option, pPLA *PLA, int out_type);
void delete_arg(int *argc, register char **argv, int num);
void init_runtime(void);
void backward_compatibility_hack(int *argc, char **argv, int *option, int *out_type);
void runtime(void);
void usage(void);
bool check_arg(int *argc, register char **argv, register char *s);

int
getopt(int nargc, char *const nargv[], const char *ostr)
{
	static char *place = EMSG;
	const char *oli;

	if (optreset || !*place) {
		optreset = 0;
		if (optind >= nargc || *(place = nargv[optind]) != '-') {
			place = EMSG;
			return (-1);
		}
		if (place[1] && *++place == '-') {
			++optind;
			place = EMSG;
			return (-1);
		}
	}
	if ((optopt = (int)*place++) == (int)':' ||
		!(oli = strchr(ostr, optopt))) {
		if (optopt == (int)'-')
			return (-1);
		if (!*place)
			++optind;
		if (opterr && *ostr != ':')
			(void)printf("illegal option -- %c\n", optopt);
		return (BADCH);
	}
	if (*++oli != ':') {
		optarg = NULL;
		if (!*place)
			++optind;
	}
	else {
		if (*place)
			optarg = place;
		else if (nargc <= ++optind) {
			place = EMSG;
			if (*ostr == ':')
				return (BADARG);
			if (opterr)
				(void)printf("option requires an argument -- %c\n", optopt);
			return (BADCH);
		}
		else
			optarg = nargv[optind];
		place = EMSG;
		++optind;
	}
	return (optopt);
}

int main(int argc, char **argv)
{
    int i, j, first, last, strategy, out_type, option;
    pPLA PLA, PLA1;
    pcover F, Fold, Dold;
    pset last1, p;
    cost_t cost;
    bool error, exact_cover;
    long start;

    start = ptime();

	error = FALSE;
	init_runtime();
#ifdef RANDOM
	srandom(314973);
#endif

	option = 0;
	out_type = F_type;
	debug = 0;
	verbose_debug = FALSE;
	print_solution = TRUE;
	summary = FALSE;
	trace = FALSE;
	strategy = 0;
	first = -1;
	last = -1;
	remove_essential = TRUE;
	force_irredundant = TRUE;
	unwrap_onset = TRUE;
	single_expand = FALSE;
	pos = FALSE;
	recompute_onset = FALSE;
	use_super_gasp = FALSE;
	use_random_order = FALSE;
	kiss = FALSE;
	echo_comments = TRUE;
	echo_unknown_commands = TRUE;
	exact_cover = FALSE;

	backward_compatibility_hack(&argc, argv, &option, &out_type);

	/* Handle -selftest before normal option processing. */
	{
		int k;
		for (k = 1; k < argc; k++) {
			if (strcmp(argv[k], "-selftest") == 0) {
				int generate = 0;
				int next = k + 1;
				if (next < argc && strcmp(argv[next], "generate") == 0) {
					generate = 1;
					next++;
				}
				{
					const char *dir = (next < argc && argv[next][0] != '-')
					                  ? argv[next] : NULL;
					exit(generate ? run_selftest_generate(dir)
					              : run_selftest(dir));
				}
			}
		}
	}

	while ((i = getopt(argc, argv, "D:S:de:o:r:stv:x")) != EOF) {
	switch(i) {
		case 'D':
		for(j = 0; option_table[j].name != 0; j++) {
			if (strcmp(optarg, option_table[j].name) == 0) {
			option = j;
			break;
			}
		}
		if (option_table[j].name == 0) {
			fprintf(stderr, "%s: bad subcommand \"%s\"\n",
			argv[0], optarg);
			exit(1);
		}
		break;

		case 'o':
		for(j = 0; pla_types[j].key != 0; j++) {
			if (strcmp(optarg, pla_types[j].key+1) == 0) {
			out_type = pla_types[j].value;
			break;
			}
		}
		if (pla_types[j].key == 0) {
			fprintf(stderr, "%s: bad output type \"%s\"\n",
			argv[0], optarg);
			exit(1);
		}
		break;

		case 'e':
		for(j = 0; esp_opt_table[j].name != 0; j++) {
			if (strcmp(optarg, esp_opt_table[j].name) == 0) {
			*(esp_opt_table[j].variable) = esp_opt_table[j].value;
			break;
			}
		}
		if (esp_opt_table[j].name == 0) {
			fprintf(stderr, "%s: bad espresso option \"%s\"\n",
			argv[0], optarg);
			exit(1);
		}
		break;

		case 'd':
		debug = debug_table[0].value;
		trace = TRUE;
		summary = TRUE;
		break;

		case 'v':
		verbose_debug = TRUE;
		for(j = 0; debug_table[j].name != 0; j++) {
			if (strcmp(optarg, debug_table[j].name) == 0) {
			debug |= debug_table[j].value;
			break;
			}
		}
		if (debug_table[j].name == 0) {
			fprintf(stderr, "%s: bad debug type \"%s\"\n",
			argv[0], optarg);
			exit(1);
		}
		break;

		case 't':
		trace = TRUE;
		break;

		case 's':
		summary = TRUE;
		break;

		case 'x':
		print_solution = FALSE;
		break;

		case 'S':
		strategy = atoi(optarg);
		break;

		case 'r':
		if (sscanf(optarg, "%d-%d", &first, &last) < 2) {
			fprintf(stderr, "%s: bad output range \"%s\"\n",
			argv[0], optarg);
			exit(1);
		}
		break;

		default:
		usage();
		exit(1);
	}
	}

	if (summary || trace) {
	printf("#");
	for(i = 0; i < argc; i++) {
		printf(" %s", argv[i]);
	}
	printf("\n");
	printf("# %s\n", VERSION);
	}

	PLA = PLA1 = NIL(PLA_t);
    switch(option_table[option].num_plas) {
	case 2:
	    if (optind+2 < argc) fatal("trailing arguments on command line");
	    getPLA(optind++, argc, argv, option, &PLA, out_type);
	    getPLA(optind++, argc, argv, option, &PLA1, out_type);
	    break;
	case 1:
	    if (optind+1 < argc) fatal("trailing arguments on command line");
	    getPLA(optind++, argc, argv, option, &PLA, out_type);
	    break;
    }
    if (optind < argc) fatal("trailing arguments on command line");

    if (summary || trace) {
	if (PLA != NIL(PLA_t)) PLA_summary(PLA);
	if (PLA1 != NIL(PLA_t)) PLA_summary(PLA1);
    }

switch(option_table[option].key) {

case KEY_ESPRESSO:
	Fold = sf_save(PLA->F);
	PLA->F = espresso(PLA->F, PLA->D, PLA->R);
	EXECUTE(error=verify(PLA->F,Fold,PLA->D), VERIFY_TIME, PLA->F, cost);
	if (error) {
	    print_solution = FALSE;
	    PLA->F = Fold;
	    (void) check_consistency(PLA);
	} else {
	    free_cover(Fold);
	}
	break;

    case KEY_MANY_ESPRESSO: {
	int pla_type;
	do {
	    EXEC(PLA->F=espresso(PLA->F,PLA->D,PLA->R),"ESPRESSO   ",PLA->F);
	    if (print_solution) {
		fprint_pla(stdout, PLA, out_type);
		(void) fflush(stdout);
	    }
	    pla_type = PLA->pla_type;
	    free_PLA(PLA);
	    setdown_cube();
	    FREE(cube.part_size);
	} while (read_pla(last_fp, TRUE, TRUE, pla_type, &PLA) != EOF);
	exit(0);
    }

	case KEY_simplify:
	EXEC(PLA->F = simplify(cube1list(PLA->F)), "SIMPLIFY  ", PLA->F);
	break;

	case KEY_so:
	if (strategy < 0 || strategy > 1) {
	    strategy = 0;
	}
	so_espresso(PLA, strategy);
	break;

	case KEY_so_both:
	if (strategy < 0 || strategy > 1) {
	    strategy = 0;
	}
	so_both_espresso(PLA, strategy);
	break;

	case KEY_expand:
	EXECUTE(PLA->F=expand(PLA->F,PLA->R,FALSE),EXPAND_TIME, PLA->F, cost);
	break;

	case KEY_irred:
	EXECUTE(PLA->F = irredundant(PLA->F, PLA->D), IRRED_TIME, PLA->F, cost);
	break;

	case KEY_reduce:
	EXECUTE(PLA->F = reduce(PLA->F, PLA->D), REDUCE_TIME, PLA->F, cost);
	break;

	case KEY_essen:
	foreach_set(PLA->F, last1, p) {
	    SET(p, RELESSEN);
	    RESET(p, NONESSEN);
	}
	EXECUTE(F = essential(&(PLA->F), &(PLA->D)), ESSEN_TIME, PLA->F, cost);
	free_cover(F);
	break;

    case KEY_super_gasp:
	PLA->F = super_gasp(PLA->F, PLA->D, PLA->R, &cost);
	break;

    case KEY_gasp:
	PLA->F = last_gasp(PLA->F, PLA->D, PLA->R, &cost);
	break;

	case KEY_make_sparse:
	PLA->F = make_sparse(PLA->F, PLA->D, PLA->R);
	break;

    case KEY_exact:
	exact_cover = TRUE;

    case KEY_qm:
	Fold = sf_save(PLA->F);
	PLA->F = minimize_exact(PLA->F, PLA->D, PLA->R, exact_cover);
	EXECUTE(error=verify(PLA->F,Fold,PLA->D), VERIFY_TIME, PLA->F, cost);
	if (error) {
	    print_solution = FALSE;
	    PLA->F = Fold;
	    (void) check_consistency(PLA);
	}
	free_cover(Fold);
	break;

	case KEY_primes:
	EXEC(PLA->F = primes_consensus(cube2list(PLA->F, PLA->D)), 
							"PRIMES     ", PLA->F);
	break;

	case KEY_map:
	map(PLA->F);
	print_solution = FALSE;
	break;

    case KEY_signature:
	Fold = sf_save(PLA->F);
	PLA->F = signature(PLA->F, PLA->D, PLA->R);
	EXECUTE(error=verify(PLA->F,Fold,PLA->D), VERIFY_TIME, PLA->F, cost);
	if (error) {
	    print_solution = FALSE;
	    PLA->F = Fold;
	    (void) check_consistency(PLA);
	} else {
	    free_cover(Fold);
	}
	break;

case KEY_opo:
	phase_assignment(PLA, strategy);
	break;

	case KEY_opoall:
	if (first < 0 || first >= cube.part_size[cube.output]) {
	    first = 0;
	}
	if (last < 0 || last >= cube.part_size[cube.output]) {
	    last = cube.part_size[cube.output] - 1;
	}
	opoall(PLA, first, last, strategy);
	break;

	case KEY_pair:
	find_optimal_pairing(PLA, strategy);
	break;

	case KEY_pairall:
	pair_all(PLA, strategy);
	break;


case KEY_echo:
break;

case KEY_taut:
	printf("ON-set is%sa tautology\n",
	    tautology(cube1list(PLA->F)) ? " " : " not ");
	print_solution = FALSE;
	break;

	case KEY_contain:
	PLA->F = sf_contain(PLA->F);
	break;

	case KEY_intersect:
	PLA->F = cv_intersect(PLA->F, PLA1->F);
	break;

	case KEY_union:
	PLA->F = sf_union(PLA->F, PLA1->F);
	break;

	case KEY_disjoint:
	PLA->F = make_disjoint(PLA->F);
	break;

	case KEY_dsharp:
	PLA->F = cv_dsharp(PLA->F, PLA1->F);
	break;

	case KEY_sharp:
	PLA->F = cv_sharp(PLA->F, PLA1->F);
	break;

	case KEY_lexsort:
	PLA->F = lex_sort(PLA->F);
	break;

	case KEY_stats:
	if (! summary) PLA_summary(PLA);
	print_solution = FALSE;
	break;

	case KEY_minterms:
	if (first < 0 || first >= cube.num_vars) {
	    first = 0;
	}
	if (last < 0 || last >= cube.num_vars) {
	    last = cube.num_vars - 1;
	}
	PLA->F = sf_dupl(unravel_range(PLA->F, first, last));
	break;

	case KEY_d1merge:
	if (first < 0 || first >= cube.num_vars) {
	    first = 0;
	}
	if (last < 0 || last >= cube.num_vars) {
	    last = cube.num_vars - 1;
	}
	for(i = first; i <= last; i++) {
	    PLA->F = d1merge(PLA->F, i);
	}
	break;

	case KEY_d1merge_in:
	for(i = 0; i < cube.num_binary_vars; i++) {
	    PLA->F = d1merge(PLA->F, i);
	}
	break;

	case KEY_PLA_verify:
	EXECUTE(error = PLA_verify(PLA, PLA1), VERIFY_TIME, PLA->F, cost);
	if (error) {
	    printf("PLA comparison failed; the PLA's are not equivalent\n");
	    exit(1);
	} else {
	    printf("PLA's compared equal\n");
	    exit(0);
	}
	break;

	case KEY_verify:
	Fold = PLA->F;	Dold = PLA->D;	F = PLA1->F;
	EXECUTE(error=verify(F, Fold, Dold), VERIFY_TIME, PLA->F, cost);
	if (error) {
	    printf("PLA comparison failed; the PLA's are not equivalent\n");
	    exit(1);
	} else {
	    printf("PLA's compared equal\n");
	    exit(0);
	}
	break;

	case KEY_check:
	(void) check_consistency(PLA);
	print_solution = FALSE;
	break;

	case KEY_mapdc:
	map_dcset(PLA);
	out_type = FD_type;
	break;

    case KEY_equiv:
	find_equiv_outputs(PLA);
	print_solution = FALSE;
	break;

	case KEY_separate:
	PLA->F = complement(cube2list(PLA->D, PLA->R));
	break;

    case KEY_xor: {
	pcover T1 = cv_intersect(PLA->F, PLA1->R);
	pcover T2 = cv_intersect(PLA1->F, PLA->R);
	free_cover(PLA->F);
	PLA->F = sf_contain(sf_join(T1, T2));
	free_cover(T1);
	free_cover(T2);
	break;
    }

    case KEY_fsm: {
	disassemble_fsm(PLA, summary);
	print_solution = FALSE;
	break;
    }

    case KEY_test: {
	pcover T, E;
	T = sf_join(PLA->D, PLA->R);
	E = new_cover(10);
	sf_free(PLA->F);
	EXECUTE(PLA->F = complement(cube1list(T)), COMPL_TIME, PLA->F, cost);
	EXECUTE(PLA->F = expand(PLA->F, T, FALSE), EXPAND_TIME, PLA->F, cost);
	EXECUTE(PLA->F = irredundant(PLA->F, E), IRRED_TIME, PLA->F, cost);
	sf_free(T);
	T = sf_join(PLA->F, PLA->R);
	EXECUTE(PLA->D = expand(PLA->D, T, FALSE), EXPAND_TIME, PLA->D, cost);
	EXECUTE(PLA->D = irredundant(PLA->D, E), IRRED_TIME, PLA->D, cost);
	sf_free(T);
	sf_free(E);
	break;
    }


    }

	if (trace) {
	runtime();
	}

	if (summary || trace) {
	print_trace(PLA->F, option_table[option].name, ptime()-start);
	}

	if (print_solution) {
	EXECUTE(fprint_pla(stdout, PLA, out_type), WRITE_TIME, PLA->F, cost);
	}

	if (error) {
	fatal("cover verification failed");
	}

	free_PLA(PLA);
	FREE(cube.part_size);
	setdown_cube();
	sf_cleanup();
	sm_cleanup();

    exit(0);
    return 0;
}


void getPLA(int opt, int argc, char **argv, int option, pPLA *PLA, int out_type)
{
    FILE *fp;
    int needs_dcset, needs_offset;
    char *fname;

    if (opt >= argc) {
	fp = stdin;
	fname = "(stdin)";
    } else {
	fname = argv[opt];
	if (strcmp(fname, "-") == 0) {
	    fp = stdin;
	} else if ((fp = fopen(argv[opt], "r")) == NULL) {
	    fprintf(stderr, "%s: Unable to open %s\n", argv[0], fname);
	    exit(1);
	}
    }
    if (option_table[option].key == KEY_echo) {
	needs_dcset = (out_type & D_type) != 0;
	needs_offset = (out_type & R_type) != 0;
    } else {
	needs_dcset = option_table[option].needs_dcset;
	needs_offset = option_table[option].needs_offset;
    }

    if (read_pla(fp, needs_dcset, needs_offset, input_type, PLA) == EOF) {
	fprintf(stderr, "%s: Unable to find PLA on file %s\n", argv[0], fname);
	exit(1);
    }
	(*PLA)->filename = _strdup(fname);
	filename = (*PLA)->filename;
	last_fp = fp; /* keep open to support -Dmany */
}


void runtime(void)
{
    int i;
    long total = 1, temp;

    for(i = 0; i < TIME_COUNT; i++) {
	total += total_time[i];
    }
    for(i = 0; i < TIME_COUNT; i++) {
	if (total_calls[i] != 0) {
	    temp = 100 * total_time[i];
	    printf("# %s\t%2d call(s) for %s (%2ld.%01ld%%)\n",
		total_name[i], total_calls[i], print_time(total_time[i]),
		    temp/total, (10 * (temp%total)) / total);
	}
    }
}


void init_runtime(void)
{
    total_name[READ_TIME] =     "READ       ";
    total_name[WRITE_TIME] =    "WRITE      ";
    total_name[COMPL_TIME] =    "COMPL      ";
    total_name[REDUCE_TIME] =   "REDUCE     ";
    total_name[EXPAND_TIME] =   "EXPAND     ";
    total_name[ESSEN_TIME] =    "ESSEN      ";
    total_name[IRRED_TIME] =    "IRRED      ";
    total_name[GREDUCE_TIME] =  "REDUCE_GASP";
    total_name[GEXPAND_TIME] =  "EXPAND_GASP";
    total_name[GIRRED_TIME] =   "IRRED_GASP ";
    total_name[MV_REDUCE_TIME] ="MV_REDUCE  ";
    total_name[RAISE_IN_TIME] = "RAISE_IN   ";
    total_name[VERIFY_TIME] =   "VERIFY     ";
    total_name[PRIMES_TIME] =   "PRIMES     ";
    total_name[MINCOV_TIME] =   "MINCOV     ";
}


void subcommands(void)
{
    int i, col;
    printf("                ");
    col = 16;
    for(i = 0; option_table[i].name != 0; i++) {
	if ((col + strlen(option_table[i].name) + 1) > 76) {
	    printf(",\n                ");
	    col = 16;
	} else if (i != 0) {
	    printf(", ");
	}
	printf("%s", option_table[i].name);
	col += strlen(option_table[i].name) + 2;
    }
    printf("\n");
}


void usage(void)
{
    printf("%s\n\n", VERSION);
    printf("SYNOPSIS: espresso [options] [file]\n\n");
    printf("  -d        Enable debugging\n");
    printf("  -e[opt]   Select espresso option:\n");
    printf("                fast, ness, nirr, nunwrap, onset, pos, strong,\n");
    printf("                eat, eatdots, kiss, random\n");
    printf("  -o[type]  Select output format:\n");
    printf("                f, r, d, fd, fr, dr, fdr,\n");
    printf("                fc, rc, dc, fdc, frc, drc, fdrc,\n");
    printf("                pleasure, eqn, eqntott, kiss, cons, scons\n");
    printf("  -rn-m     Select range for subcommands:\n");
    printf("                d1merge: first and last variables (0 ... m-1)\n");
    printf("                minterms: first and last variables (0 ... m-1)\n");
    printf("                opoall: first and last outputs (0 ... m-1)\n");
    printf("  -s        Provide short execution summary\n");
    printf("  -t        Provide longer execution trace\n");
    printf("  -x        Suppress printing of solution\n");
    printf("  -v[type]  Verbose debugging detail (-v '' for all)\n");
	printf("  -D[cmd]   Execute subcommand 'cmd':\n");
	subcommands();
	printf("  -Sn       Select strategy for subcommands:\n");
	printf("                opo: bit2=exact bit1=repeated bit0=skip sparse\n");
	printf("                opoall: 0=minimize, 1=exact\n");
	printf("                pair: 0=algebraic, 1=strongd, 2=espresso, 3=exact\n");
	printf("                pairall: 0=minimize, 1=exact, 2=opo\n");
	printf("                so / single_output: 0=minimize, 1=exact\n");
	printf("                so_both: 0=minimize, 1=exact\n");
	printf("  -selftest <dir>          Run regression selftest (reads <dir>/hash.txt)\n");
	printf("  -selftest generate <dir> Compute hashes for all files under <dir>,\n");
	printf("                           write <dir>/hash.txt  (default dir: tests)\n");
}

void backward_compatibility_hack(int *argc, char **argv, int *option, int *out_type)
{
	int i, j;

	*option = 0;
    for(i = 1; i < (*argc)-1; i++) {
	if (strcmp(argv[i], "-do") == 0) {
	    for(j = 0; option_table[j].name != 0; j++)
		if (strcmp(argv[i+1], option_table[j].name) == 0) {
		    *option = j;
		    delete_arg(argc, argv, i+1);
		    delete_arg(argc, argv, i);
		    break;
		}
	    if (option_table[j].name == 0) {
		fprintf(stderr,
		 "espresso: bad keyword \"%s\" following -do\n",argv[i+1]);
		exit(1);
	    }
	    break;
	}
    }

    for(i = 1; i < (*argc)-1; i++) {
	if (strcmp(argv[i], "-out") == 0) {
	    for(j = 0; pla_types[j].key != 0; j++)
		if (strcmp(pla_types[j].key+1, argv[i+1]) == 0) {
		    *out_type = pla_types[j].value;
		    delete_arg(argc, argv, i+1);
		    delete_arg(argc, argv, i);
		    break;
		}
	    if (pla_types[j].key == 0) {
		fprintf(stderr,
		   "espresso: bad keyword \"%s\" following -out\n",argv[i+1]);
		exit(1);
	    }
	    break;
	}
    }

    for(i = 1; i < (*argc); i++) {
	if (argv[i][0] == '-') {
	    for(j = 0; esp_opt_table[j].name != 0; j++) {
		if (strcmp(argv[i]+1, esp_opt_table[j].name) == 0) {
		    delete_arg(argc, argv, i);
		    *(esp_opt_table[j].variable) = esp_opt_table[j].value;
		    break;
		}
	    }
	}
    }

    if (check_arg(argc, argv, "-fdr")) input_type = FDR_type;
    if (check_arg(argc, argv, "-fr")) input_type = FR_type;
    if (check_arg(argc, argv, "-f")) input_type = F_type;
}


void delete_arg(int *argc, register char **argv, int num)
{
    register int i;
    (*argc)--;
    for(i = num; i < *argc; i++) {
	argv[i] = argv[i+1];
    }
}


bool check_arg(int *argc, register char **argv, register char *s)
{
    register int i;
    for(i = 1; i < *argc; i++) {
	if (strcmp(argv[i], s) == 0) {
	    delete_arg(argc, argv, i);
	    return TRUE;
	}
    }
    return FALSE;
}
