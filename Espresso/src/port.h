#pragma once

#include <limits.h>
#include <stdlib.h>
#include <string.h>
#include <assert.h>

/*
 * int32 should be defined as the most economical sized integer capable of
 * holding a 32 bit quantity
 * int16 should be similarly defined
 */

/* AB Stuff */

typedef int (* qsort_compare_func)(const void *, const void *);

/* XXX hack */
#ifndef MACHDEP_INCLUDED
#define MACHDEP_INCLUDED
#ifdef vax
typedef int int32;
typedef short int16;
#else
     /* Ansi-C promises that these definitions should always work */
typedef long int32;
typedef int int16;
#endif /* vax */
#endif /* MACHDEP_INCLUDED */


#ifndef __STDC__
#ifndef __DATE__
#ifdef CUR_DATE
#define __DATE__	CUR_DATE
#else
#define __DATE__	"unknown-date"
#endif /* CUR_DATE */
#endif /* __DATE__ */

#ifndef __TIME__
#ifdef CUR_TIME
#define __TIME__	CUR_TIME
#else
#define __TIME__	"unknown-time"
#endif /* CUR_TIME */
#endif /* __TIME__ */
#endif /* __STDC__ */

#ifdef sun386
#define PORTAR
#endif

#include <stdio.h>
#include <ctype.h>
#include <sys/types.h>
#undef HUGE
#include <math.h>
#include <signal.h>

#ifndef NULL
#define NULL 0
#endif /* NULL */

/*
 * CHARBITS should be defined only if the compiler lacks "unsigned char".
 * It should be a mask, e.g. 0377 for an 8-bit machine.
 */

#ifndef CHARBITS
#	define	UNSCHAR(c)	((unsigned char)(c))
#else
#	define	UNSCHAR(c)	((c)&CHARBITS)
#endif

#define SIZET int

#define CONST const
#define VOIDSTAR   void *



