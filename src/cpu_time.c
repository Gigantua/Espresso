/* LINTLIBRARY */
#include "port.h"
#include "utility.h"


/*
 *   util_cpu_time -- return a long which represents the elapsed processor
 *   time in milliseconds since some constant reference
 */
long util_cpu_time(void)
{
    long t = 0;

#ifdef BSD
    struct rusage rusage;
    (void) getrusage(RUSAGE_SELF, &rusage);
    t = (long) rusage.ru_utime.tv_sec*1000 + rusage.ru_utime.tv_usec/1000;
#endif

#ifdef UNIX10			/* times() with 10 Hz resolution */
    struct tms buffer;
    (void) times(&buffer);
    t = buffer.tms_utime * 100;
#endif

#ifdef UNIX60			/* times() with 60 Hz resolution */
    struct tms buffer;
    times(&buffer);
    t = buffer.tms_utime * 16.6667;
#endif

#ifdef UNIX100
    struct tms buffer;		/* times() with 100 Hz resolution */
    times(&buffer);
    t = buffer.tms_utime * 10;
#endif

    return t;
}
